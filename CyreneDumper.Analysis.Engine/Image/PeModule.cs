using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace CyreneDumper.Analysis.Engine.Image;

internal sealed class PeModule : IDisposable
{
    private readonly Stream _stream;
    private readonly PEReader _pe;
    private IReadOnlyDictionary<uint, uint>? _functions;
    public ulong ImageBase { get; }
    public IReadOnlyDictionary<uint, uint> Functions => _functions ??= ReadFunctionTable();
    public IEnumerable<SectionHeader> ExecutableSections =>
        _pe.PEHeaders.SectionHeaders.Where(section => section.SizeOfRawData > 0
            && (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0);

    public PeModule(Stream stream)
    {
        _stream = stream;
        try
        {
            _pe = new(_stream, PEStreamOptions.LeaveOpen);
            if (_pe.PEHeaders.PEHeader is not { Magic: PEMagic.PE32Plus } header || _pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                throw new InvalidDataException("原生分析当前要求 x64 PE。");
            ImageBase = header.ImageBase;
        }
        catch { _stream.Dispose(); throw; }
    }

    public ulong NormalizeAddress(ulong address) => address < ImageBase ? checked(ImageBase + address) : address;

    public byte[] Read(ulong address, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        address = NormalizeAddress(address);
        ulong rva = address - ImageBase;
        foreach (SectionHeader section in _pe.PEHeaders.SectionHeaders)
        {
            long relative = checked((long)rva - section.VirtualAddress);
            if (relative < 0 || relative >= section.SizeOfRawData) continue;
            int count = (int)Math.Min(maximumBytes, section.SizeOfRawData - relative);
            byte[] bytes = new byte[count];
            _stream.Position = section.PointerToRawData + relative;
            _stream.ReadExactly(bytes);
            return bytes;
        }
        throw new InvalidDataException($"地址不在 PE 文件节中：0x{address:x}");
    }

    public byte[]? ReadConstant(ulong address, int size)
    {
        address = NormalizeAddress(address);
        ulong rva = address - ImageBase;
        foreach (SectionHeader section in _pe.PEHeaders.SectionHeaders)
        {
            long offset = checked((long)rva - section.VirtualAddress);
            if (offset >= 0 && offset + size <= section.SizeOfRawData
                && (section.SectionCharacteristics & SectionCharacteristics.MemWrite) == 0) return Read(address, size);
        }
        return null;
    }

    private IReadOnlyDictionary<uint, uint> ReadFunctionTable()
    {
        DirectoryEntry directory = _pe.PEHeaders.PEHeader!.ExceptionTableDirectory;
        if (directory.Size < 0 || directory.Size % 12 != 0)
            throw new InvalidDataException("PE 缺少有效的 x64 函数范围表。");
        byte[] table = directory.Size == 0 ? [] : Read((ulong)directory.RelativeVirtualAddress, directory.Size);
        if (table.Length != directory.Size) throw new InvalidDataException("x64 函数范围表被截断。");

        Dictionary<uint, uint> functions = [];
        for (int index = 0; index < table.Length; index += 12)
            functions.TryAdd(BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(index)),
                BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(index + 4)));
        return functions;
    }

    public void Dispose()
    {
        _pe.Dispose();
        _stream.Dispose();
    }
}
