using CyreneDumper.Analysis.Engine.Image;
using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Disassembly;

internal sealed class FunctionReader
{
    private const int MaximumFunctionBytes = 1_048_576;
    private const int MaximumLeafBytes = 4096;
    private readonly PeModule _module;
    private readonly RelativeJumpTableReader _jumpTables;
    private readonly ControlFlowEdges _edges;
    private readonly Dictionary<ulong, bool> _nonReturning = [];
    private readonly Dictionary<ulong, NativeSwitch> _switches = [];

    public FunctionReader(PeModule module)
    {
        _module = module;
        _jumpTables = new(module);
        _edges = new(SwitchAt, IsNonReturning);
    }

    public NativeSwitch? SwitchAt(ulong instruction) => _switches.GetValueOrDefault(instruction);

    public Instruction[] ReadBlock(ulong address, int maximumBytes)
    {
        byte[] bytes = _module.Read(address, maximumBytes);
        Decoder decoder = OpenDecoder(bytes, _module.NormalizeAddress(address), 0);
        ulong end = decoder.IP + (uint)bytes.Length;
        List<Instruction> instructions = [];
        while (decoder.IP < end)
        {
            decoder.Decode(out Instruction instruction);
            if (instruction.IsInvalid) break;
            instructions.Add(instruction);
            if (instruction.FlowControl is FlowControl.Return or FlowControl.UnconditionalBranch or FlowControl.Interrupt or FlowControl.Exception) break;
        }
        return instructions.ToArray();
    }

    public NativeControlFlowGraph BuildGraph(ulong address)
    {
        ulong entry = _module.NormalizeAddress(address);
        return new(entry, ReadFunction(entry), _edges);
    }

    public Instruction[] ReadFunction(ulong address)
    {
        ulong entry = _module.NormalizeAddress(address);
        return TryGetEnd(entry, out ulong end) ? ReadBoundedFunction(new(entry, ReadBody(entry, end))) : ReadLeaf(entry);
    }

    public Instruction[] ReadFunctionBody(ulong address)
    {
        ulong entry = _module.NormalizeAddress(address);
        if (!TryGetEnd(entry, out ulong end))
            throw new InvalidDataException($"No x64 function starts at 0x{entry:X}.");
        Decoder decoder = OpenDecoder(ReadBody(entry, end), entry, 0);
        List<Instruction> instructions = [];
        while (decoder.IP < end)
        {
            decoder.Decode(out Instruction instruction);
            if (instruction.IsInvalid || instruction.NextIP > end)
                throw new InvalidDataException($"Invalid instruction at 0x{instruction.IP:X}.");
            instructions.Add(instruction);
        }
        return instructions.ToArray();
    }

    // 单个路径可用于确认引用位置，不要求同一函数的所有间接跳转都能恢复。
    public Instruction[] ReadPath(ulong address, ulong probe)
    {
        ulong entry = _module.NormalizeAddress(address);
        probe = _module.NormalizeAddress(probe);
        if (!TryGetEnd(entry, out ulong end) || probe < entry || probe >= end)
            throw new InvalidDataException("目标指令没有同一 x64 函数范围。");

        FunctionBytes body = new(entry, ReadBody(entry, end));
        Dictionary<ulong, Instruction> instructions = [];
        Dictionary<ulong, ulong?> parents = new() { [entry] = null };
        Queue<ulong> pending = new();
        pending.Enqueue(entry);
        while (pending.TryDequeue(out ulong addressToRead))
        {
            Instruction instruction = body.Decode(addressToRead);
            if (instruction.IsInvalid || instruction.NextIP > body.End) continue;
            if (instructions.Values.Any(previous => previous.IP < instruction.NextIP && instruction.IP < previous.NextIP))
                throw new InvalidDataException("目标路径存在跳入指令内部的分支。");

            instructions.Add(addressToRead, instruction);
            if (probe >= instruction.IP && probe < instruction.NextIP)
                return NativeControlFlowGraph.RestorePath(instructions, parents, addressToRead);
            foreach (ulong successor in _edges.From(instruction, stopAtNoReturn: true))
                if (body.Contains(successor) && parents.TryAdd(successor, addressToRead)) pending.Enqueue(successor);
        }
        throw new InvalidDataException("目标指令不能通过已核实的控制流到达。");
    }

    public bool IsNonReturning(ulong address)
    {
        address = _module.NormalizeAddress(address);
        if (_nonReturning.TryGetValue(address, out bool known)) return known;
        // 只接受直达 trap 的基本块。
        Instruction[] code = ReadBlock(address, 512);
        bool proven = code.Length > 0 && code[^1].FlowControl is FlowControl.Interrupt or FlowControl.Exception
            && code.SkipLast(1).All(instruction => instruction.FlowControl is FlowControl.Next or FlowControl.Call or FlowControl.IndirectCall);
        _nonReturning.Add(address, proven);
        return proven;
    }

    private Instruction[] ReadBoundedFunction(FunctionBytes body)
    {
        Dictionary<ulong, Instruction> instructions = [];
        Queue<ulong> pending = new();
        Dictionary<ulong, NativeSwitch> switches = [];
        pending.Enqueue(body.Entry);
        while (pending.TryDequeue(out ulong address))
        {
            if (instructions.ContainsKey(address) || !body.Contains(address)) continue;
            Instruction instruction = body.Decode(address);
            if (instruction.IsInvalid || instruction.NextIP > body.End)
                throw new InvalidDataException($"原生函数包含无法解析的指令：0x{address:x}");

            instructions.Add(address, instruction);
            if (instruction.FlowControl == FlowControl.IndirectBranch)
            {
                NativeSwitch table = _jumpTables.Read(instructions.Values.OrderBy(item => item.IP).ToArray(), instruction);
                if (table.Targets.Any(target => !body.Contains(target)))
                    throw new InvalidDataException("原生跳表目标超出当前函数范围。");
                switches.Add(instruction.IP, table);
                foreach (ulong target in table.Targets) pending.Enqueue(target);
                continue;
            }
            foreach (ulong successor in _edges.From(instruction, stopAtNoReturn: false))
                pending.Enqueue(successor);
        }

        Instruction[] ordered = OrderAndCheck(instructions.Values);
        // 完整函数解码后复核边，防止回边跳过跳表的上界检查。
        foreach (ulong jump in switches.Keys.ToArray())
            switches[jump] = _jumpTables.Read(ordered, instructions[jump]);
        foreach ((ulong jump, NativeSwitch table) in switches)
        {
            _switches[table.Load] = table;
            _switches[jump] = table;
        }
        return ordered;
    }

    private Instruction[] ReadLeaf(ulong entry)
    {
        FunctionBytes body = new(entry, _module.Read(entry, MaximumLeafBytes));
        Dictionary<ulong, Instruction> instructions = [];
        Queue<ulong> pending = new();
        pending.Enqueue(entry);
        bool returned = false;
        while (pending.TryDequeue(out ulong address))
        {
            if (instructions.ContainsKey(address)) continue;
            if (!body.Contains(address)) throw new InvalidDataException("叶函数控制流超出可验证范围。");
            Instruction instruction = body.Decode(address);
            if (instruction.IsInvalid || instruction.FlowControl is FlowControl.Call or FlowControl.IndirectCall
                or FlowControl.IndirectBranch or FlowControl.Interrupt or FlowControl.Exception || instruction.Mnemonic == Mnemonic.Push)
                throw new InvalidDataException("缺少 x64 边界的函数不符合可核实叶函数条件。");

            instructions.Add(address, instruction);
            if (instruction.FlowControl == FlowControl.Return || IsLeafTailCall(body, instruction))
            {
                returned = true;
                continue;
            }
            foreach (ulong successor in _edges.From(instruction, stopAtNoReturn: false))
                pending.Enqueue(successor);
        }
        if (!returned) throw new InvalidDataException("叶函数没有可核实的返回路径。");
        return OrderAndCheck(instructions.Values);
    }

    private bool IsLeafTailCall(FunctionBytes body, Instruction instruction)
        => instruction.FlowControl == FlowControl.UnconditionalBranch && instruction.Op0Kind == OpKind.NearBranch64
            && (!body.Contains(instruction.NearBranchTarget) || TryGetEnd(instruction.NearBranchTarget, out _));

    private bool TryGetEnd(ulong entry, out ulong end)
    {
        bool exists = _module.Functions.TryGetValue(checked((uint)(entry - _module.ImageBase)), out uint relative);
        end = _module.ImageBase + relative;
        return exists;
    }

    private byte[] ReadBody(ulong entry, ulong end)
    {
        if (end <= entry || end - entry > MaximumFunctionBytes)
            throw new InvalidDataException($"没有可核实的函数范围：0x{entry:x}");
        int size = checked((int)(end - entry));
        byte[] bytes = _module.Read(entry, size);
        if (bytes.Length != size) throw new InvalidDataException("原生函数范围被截断。");
        return bytes;
    }

    private static Instruction[] OrderAndCheck(IEnumerable<Instruction> instructions)
    {
        Instruction[] ordered = instructions.OrderBy(instruction => instruction.IP).ToArray();
        for (int index = 1; index < ordered.Length; index++)
            if (ordered[index].IP < ordered[index - 1].NextIP)
                throw new InvalidDataException("原生分支跳入指令内部。");
        return ordered;
    }

    private static Decoder OpenDecoder(byte[] bytes, ulong address, int offset)
        => Decoder.Create(64, new ByteArrayCodeReader(bytes, offset, bytes.Length - offset), address);

    private sealed class FunctionBytes(ulong entry, byte[] bytes)
    {
        public ulong Entry { get; } = entry;
        public ulong End => Entry + (uint)bytes.Length;
        public bool Contains(ulong address) => address >= Entry && address < End;

        public Instruction Decode(ulong address)
        {
            Decoder decoder = OpenDecoder(bytes, address, checked((int)(address - Entry)));
            decoder.Decode(out Instruction instruction);
            return instruction;
        }
    }
}
