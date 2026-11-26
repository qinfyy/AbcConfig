using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using CyreneDumper.Analysis.Engine.Disassembly;
using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Image;

public sealed record NativeReference(ulong Function, ulong Site, ulong Target);

internal sealed class NativeReferenceSearch(PeModule module, FunctionReader functions)
{
    // 先定位相对引用，再核对当前函数的指令边界；字节命中本身不算调用证据。
    public NativeReference[] Find(IEnumerable<ulong> addresses, bool memory = false, Action<NativeReference, string>? unresolved = null)
    {
        IReadOnlyDictionary<uint, uint> ranges = module.Functions;
        HashSet<ulong> targets = addresses.Select(module.NormalizeAddress).ToHashSet();
        uint[] starts = ranges.Keys.Order().ToArray();
        Dictionary<ulong, List<(ulong Site, ulong Target)>> candidates = [];
        foreach (SectionHeader section in module.ExecutableSections)
        {
            byte[] data = module.Read((ulong)section.VirtualAddress, section.SizeOfRawData);
            for (int index = memory ? 0 : 1; index + 4 <= data.Length; index++)
            {
                if (!memory && data[index - 1] is not (0xe8 or 0xe9)) continue;
                ulong next = module.ImageBase + (uint)section.VirtualAddress + (uint)index + 4;
                ulong target = unchecked((ulong)((long)next + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(index, 4))));
                if (!targets.Contains(target)) continue;
                uint rva = checked((uint)(next - 4 - module.ImageBase));
                int position = Array.BinarySearch(starts, rva);
                if (position < 0) position = ~position - 1;
                if (position < 0 || rva >= ranges[starts[position]]) continue;
                ulong entry = module.ImageBase + starts[position];
                if (!candidates.TryGetValue(entry, out var values)) candidates.Add(entry, values = []);
                values.Add((next - (memory ? 4u : 5u), target));
            }
        }
        List<NativeReference> result = [];
        foreach ((ulong entry, var values) in candidates)
        {
            Instruction[] instructions;
            try { instructions = functions.ReadFunction(entry); }
            catch (InvalidDataException error)
            {
                foreach (var candidate in values)
                {
                    try
                    {
                        Instruction instruction = functions.ReadPath(entry, candidate.Site)[^1];
                        if (memory ? instruction.IsIPRelativeMemoryOperand && instruction.IPRelativeMemoryAddress == candidate.Target
                            : instruction.IP == candidate.Site && instruction.Op0Kind == OpKind.NearBranch64 && instruction.NearBranchTarget == candidate.Target)
                            result.Add(new(entry, instruction.IP, candidate.Target));
                    }
                    catch (InvalidDataException) { unresolved?.Invoke(new(entry, candidate.Site, candidate.Target), error.Message); }
                }
                continue;
            }
            foreach (Instruction instruction in instructions)
                foreach (var candidate in values)
                    if (memory ? instruction.IsIPRelativeMemoryOperand && instruction.IPRelativeMemoryAddress == candidate.Target
                        && candidate.Site >= instruction.IP && candidate.Site < instruction.NextIP
                        : instruction.IP == candidate.Site && instruction.Op0Kind == OpKind.NearBranch64 && instruction.NearBranchTarget == candidate.Target
                        && instruction.FlowControl is FlowControl.Call or FlowControl.UnconditionalBranch)
                        result.Add(new(entry, instruction.IP, candidate.Target));
        }
        return result.Distinct().OrderBy(reference => reference.Function).ThenBy(reference => reference.Site).ToArray();
    }
}
