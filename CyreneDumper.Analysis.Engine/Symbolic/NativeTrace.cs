using CyreneDumper.Analysis.Engine.Disassembly;
using CyreneDumper.Analysis.Engine.Image;
using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Symbolic;

public sealed class NativeTrace(NativeImage image, Func<ulong, NativeValue?>? resolveGlobal = null, params ulong[] allocators)
{
    private readonly HashSet<ulong> _allocators = allocators.Select(image.NormalizeAddress).ToHashSet();

    public NativeTraceResult Read(ulong entry, ulong? through = null, ulong? via = null)
        => ReadCore(entry, through, via is ulong checkpoint ? [checkpoint] : []);

    public NativeTraceResult ReadPath(ulong entry, ulong through, params ulong[] checkpoints)
        => ReadCore(entry, through, checkpoints);

    private NativeTraceResult ReadCore(ulong address, ulong? through, ulong[] checkpoints)
    {
        ulong entry = image.NormalizeAddress(address);
        Instruction[] code = through is ulong target ? SelectPath(entry, image.NormalizeAddress(target), checkpoints) : image.DecodeFunction(entry);
        return new TraceExecution(image, resolveGlobal, _allocators).Run(entry, code, through.HasValue);
    }

    private Instruction[] SelectPath(ulong entry, ulong target, ulong[] checkpoints)
    {
        NativeControlFlowGraph graph;
        try { graph = image.GetControlFlow(entry); }
        catch (InvalidDataException) when (checkpoints.Length == 0)
        {
            Instruction[] path = image.DecodePath(entry, target);
            if (path[^1].IP != target)
                throw new InvalidDataException($"目标 0x{target:x} 不在指令起点。");
            return path;
        }
        return graph.PathTo(target, checkpoints.Select(image.NormalizeAddress).ToArray());
    }
}
