using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Disassembly;

public sealed class NativeControlFlowGraph
{
    private readonly Dictionary<ulong, Instruction> _byAddress;
    private readonly ControlFlowEdges _edges;

    public ulong Entry { get; }
    public IReadOnlyList<Instruction> Instructions { get; }

    internal NativeControlFlowGraph(ulong entry, Instruction[] instructions, ControlFlowEdges edges)
    {
        Entry = entry;
        Instructions = Array.AsReadOnly(instructions);
        _byAddress = instructions.ToDictionary(instruction => instruction.IP);
        _edges = edges;
    }

    public bool TryGetInstruction(ulong address, out Instruction instruction) => _byAddress.TryGetValue(address, out instruction);

    public IEnumerable<ulong> Successors(ulong address)
    {
        if (_byAddress.TryGetValue(address, out Instruction instruction))
            foreach (ulong successor in _edges.From(instruction, stopAtNoReturn: true))
                if (_byAddress.ContainsKey(successor)) yield return successor;
    }

    public Instruction[] PathTo(ulong target, params ulong[] checkpoints)
    {
        ulong start = Entry;
        List<Instruction> result = [];
        foreach (ulong checkpoint in checkpoints)
        {
            result.AddRange(FindPath(start, checkpoint));
            start = result[^1].NextIP;
            if (!Successors(checkpoint).Contains(start))
                throw new InvalidDataException($"检查点 0x{checkpoint:x} 没有可达的后续指令。");
        }
        result.AddRange(FindPath(start, target));
        return result.ToArray();
    }

    private IEnumerable<Instruction> FindPath(ulong start, ulong target)
    {
        if (!_byAddress.ContainsKey(start) || !_byAddress.ContainsKey(target))
            throw new InvalidDataException($"目标 0x{target:x} 或入口 0x{start:x} 不在已解码指令中。");

        Dictionary<ulong, ulong?> parents = new() { [start] = null };
        Queue<ulong> pending = new();
        pending.Enqueue(start);
        while (!parents.ContainsKey(target) && pending.TryDequeue(out ulong address))
            foreach (ulong successor in Successors(address))
                if (parents.TryAdd(successor, address)) pending.Enqueue(successor);

        if (!parents.ContainsKey(target))
            throw new InvalidDataException($"目标 0x{target:x} 无法从入口 0x{start:x} 到达。");
        return RestorePath(_byAddress, parents, target);
    }

    internal static Instruction[] RestorePath(IReadOnlyDictionary<ulong, Instruction> instructions,
        IReadOnlyDictionary<ulong, ulong?> parents, ulong target)
    {
        List<Instruction> path = [];
        for (ulong? cursor = target; cursor is ulong address; cursor = parents[address])
            path.Add(instructions[address]);
        path.Reverse();
        return path.ToArray();
    }
}

internal sealed class ControlFlowEdges(Func<ulong, NativeSwitch?> findSwitch, Func<ulong, bool> isNonReturning)
{
    public IEnumerable<ulong> From(Instruction instruction, bool stopAtNoReturn)
    {
        switch (instruction.FlowControl)
        {
            case FlowControl.Return or FlowControl.Interrupt or FlowControl.Exception:
                yield break;
            case FlowControl.IndirectBranch:
                if (findSwitch(instruction.IP) is { } table)
                    foreach (ulong target in table.Targets) yield return target;
                yield break;
            case FlowControl.UnconditionalBranch:
                if (instruction.Op0Kind == OpKind.NearBranch64) yield return instruction.NearBranchTarget;
                yield break;
            case FlowControl.ConditionalBranch:
                if (instruction.Op0Kind == OpKind.NearBranch64) yield return instruction.NearBranchTarget;
                break;
            case FlowControl.Call when stopAtNoReturn && instruction.Op0Kind == OpKind.NearBranch64
                && isNonReturning(instruction.NearBranchTarget):
                yield break;
        }
        yield return instruction.NextIP;
    }
}
