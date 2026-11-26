using CyreneDumper.Analysis.Engine.Disassembly;
using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Image;

public sealed class NativeImage : IDisposable
{
    private readonly PeModule _module;
    private readonly FunctionReader _functions;

    public NativeImage(string path) : this(File.OpenRead(path)) { }

    public NativeImage(Stream stream)
    {
        _module = new(stream);
        _functions = new(_module);
    }

    public ulong ImageBase => _module.ImageBase;
    public ulong NormalizeAddress(ulong address) => _module.NormalizeAddress(address);
    public byte[] Read(ulong address, int maximumBytes) => _module.Read(address, maximumBytes);
    public byte[]? ReadConstant(ulong address, int size) => _module.ReadConstant(address, size);
    public Instruction[] Decode(ulong address, int maximumBytes = 512) => _functions.ReadBlock(address, maximumBytes);
    public Instruction[] DecodeFunction(ulong address) => _functions.ReadFunction(address);
    public Instruction[] DecodeFunctionBody(ulong address) => _functions.ReadFunctionBody(address);
    public Instruction[] DecodePath(ulong entry, ulong probe) => _functions.ReadPath(entry, probe);
    public NativeControlFlowGraph GetControlFlow(ulong entry) => _functions.BuildGraph(entry);
    public NativeSwitch? SwitchAt(ulong instruction) => _functions.SwitchAt(instruction);
    public bool IsNonReturning(ulong address) => _functions.IsNonReturning(address);

    public NativeReference[] FindReferences(IEnumerable<ulong> addresses, bool memory = false, Action<NativeReference, string>? unresolved = null)
        => new NativeReferenceSearch(_module, _functions).Find(addresses, memory, unresolved);

    public void Dispose() => _module.Dispose();
}
