using CyreneDumper.Analysis.Engine.Disassembly;
using CyreneDumper.Analysis.Engine.Image;
using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Symbolic;

internal sealed class TraceExecution(NativeImage image, Func<ulong, NativeValue?>? resolveGlobal, HashSet<ulong> allocators)
{
    private static readonly Register[] Arguments = [Register.RCX, Register.RDX, Register.R8, Register.R9];
    private static readonly Register[] Volatile = [Register.RAX, Register.RCX, Register.RDX, Register.R8, Register.R9, Register.R10, Register.R11,
        Register.XMM0, Register.XMM1, Register.XMM2, Register.XMM3, Register.XMM4, Register.XMM5];
    private readonly Dictionary<Register, NativeValue> registers = [];
    private readonly Dictionary<NativeValue, NativeValue?> memory = [];
    private readonly List<NativeCall> calls = [];
    private readonly List<NativeAccess> loads = [], stores = [];

    public NativeTraceResult Run(ulong entry, Instruction[] code, bool followsPath)
    {
        for (int index = 0; index < Arguments.Length; index++) registers.Add(Arguments[index], NativeValue.Argument(index));
        registers[Register.RSP] = new("stack");
        for (int index = 4; index < 8; index++) memory[new NativeValue("stack", 8 + index * 8)] = NativeValue.Argument(index);

        NativeValue? returned = null, floatReturned = null;
        InstructionInfoFactory information = new();
        HashSet<ulong> locations = code.Select(instruction => instruction.IP).ToHashSet();
        foreach (Instruction instruction in code)
        {
            Register destination = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetFullRegister() : Register.None;
            if (instruction.FlowControl == FlowControl.Return)
            {
                returned = registers.GetValueOrDefault(Register.RAX); floatReturned = registers.GetValueOrDefault(Register.XMM0.GetFullRegister());
                break;
            }
            if (instruction.FlowControl is FlowControl.Interrupt or FlowControl.Exception) break;
            if (instruction.Mnemonic == Mnemonic.Push)
            {
                NativeValue? value = ReadOperand(instruction, 0);
                NativeValue stack = registers[Register.RSP].At(-8);
                registers[Register.RSP] = stack; memory[stack] = value;
                continue;
            }
            if (instruction.Mnemonic == Mnemonic.Pop)
            {
                Set(destination, memory.GetValueOrDefault(registers[Register.RSP]));
                registers[Register.RSP] = registers[Register.RSP].At(8);
                continue;
            }
            if (instruction.Mnemonic is Mnemonic.Add or Mnemonic.Sub && destination != Register.None && ReadOperand(instruction, 1) is { Kind: "constant" } number)
            {
                NativeValue? value = registers.GetValueOrDefault(destination);
                long delta = instruction.Mnemonic == Mnemonic.Add ? number.Number : -number.Number;
                Set(destination, value?.Kind == "constant" ? NativeValue.Constant(value.Number + delta) : value?.At(delta));
                continue;
            }
            if (instruction.Mnemonic is Mnemonic.Inc or Mnemonic.Dec && destination != Register.None)
            {
                NativeValue? value = registers.GetValueOrDefault(destination);
                long delta = instruction.Mnemonic == Mnemonic.Inc ? 1 : -1;
                Set(destination, value?.Kind == "constant" ? NativeValue.Constant(value.Number + delta) : value?.At(delta));
                continue;
            }
            if (instruction.Mnemonic is Mnemonic.Xor or Mnemonic.Sub or Mnemonic.Xorps or Mnemonic.Xorpd or Mnemonic.Pxor
                && instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
            { Set(destination, NativeValue.Constant(0)); continue; }
            if (instruction.Mnemonic is Mnemonic.Mulss or Mnemonic.Mulps)
            {
                NativeValue? left = registers.GetValueOrDefault(destination), right = ReadOperand(instruction, 1);
                if (right is { Kind: "load" } load) right = load with { Width = instruction.MemorySize.GetSize() };
                if (left is not null && right is not null)
                    Set(destination, new(instruction.Mnemonic == Mnemonic.Mulss ? "mulss" : "mulps", Base: left, Index: right,
                        Width: instruction.Mnemonic == Mnemonic.Mulss ? 4 : 16));
                else Set(destination, null);
                continue;
            }
            if (instruction.Mnemonic is Mnemonic.Mov or Mnemonic.Movzx or Mnemonic.Movsx or Mnemonic.Movsxd or Mnemonic.Movaps or Mnemonic.Movups
                or Mnemonic.Movdqa or Mnemonic.Movdqu or Mnemonic.Movss or Mnemonic.Movsd or Mnemonic.Movlps or Mnemonic.Movd or Mnemonic.Movq or Mnemonic.Lea)
            {
                NativeValue? value = instruction.Mnemonic == Mnemonic.Lea && instruction.Op1Kind == OpKind.Memory ? AddressOf(instruction) : ReadOperand(instruction, 1);
                if (destination != Register.None)
                {
                    if (instruction.Mnemonic is Mnemonic.Movaps or Mnemonic.Movups or Mnemonic.Movdqa or Mnemonic.Movdqu
                        or Mnemonic.Movss or Mnemonic.Movsd or Mnemonic.Movlps && value is { Kind: "load" } load)
                        value = load with { Width = instruction.MemorySize.GetSize() };
                    Set(destination, value);
                }
                else if (instruction.Op0Kind == OpKind.Memory)
                {
                    NativeValue location = AddressOf(instruction); memory[location] = value;
                    stores.Add(new(instruction.IP, location, value, instruction.MemorySize.GetSize()));
                }
                continue;
            }
            if (instruction.Mnemonic is Mnemonic.Cmp or Mnemonic.Test)
            { ReadOperand(instruction, 0); ReadOperand(instruction, 1); continue; }
            if (instruction.FlowControl is FlowControl.Call or FlowControl.IndirectCall or FlowControl.UnconditionalBranch or FlowControl.IndirectBranch)
            {
                if (instruction.FlowControl == FlowControl.UnconditionalBranch && locations.Contains(instruction.NearBranchTarget)) continue;
                if (followsPath && instruction.FlowControl == FlowControl.IndirectBranch && image.SwitchAt(instruction.IP) is NativeSwitch table
                    && table.Targets.Any(locations.Contains)) continue;
                ulong? called = instruction.Op0Kind == OpKind.NearBranch64 ? instruction.NearBranchTarget : null;
                NativeValue?[] arguments = [.. Arguments.Select(register => registers.GetValueOrDefault(register)),
                    .. Enumerable.Range(4, 4).Select(index => memory.GetValueOrDefault(registers[Register.RSP].At(index * 8)))];
                calls.Add(new(instruction.IP, called, arguments));
                foreach (NativeValue argument in arguments.OfType<NativeValue>().Where(value => value.Kind == "stack"))
                    foreach (NativeValue key in memory.Keys.Where(key => key.Kind == "stack" && key.Number >= argument.Number && key.Number < argument.Number + 16).ToArray()) memory.Remove(key);
                foreach (Register register in Volatile) registers.Remove(register.GetFullRegister());
                registers[Register.RAX] = new(called.HasValue && allocators.Contains(called.Value) ? "object" : "call", unchecked((long)instruction.IP));
                registers[Register.XMM0.GetFullRegister()] = new("call", unchecked((long)instruction.IP));
                if (instruction.FlowControl is FlowControl.UnconditionalBranch or FlowControl.IndirectBranch)
                {
                    returned = registers.GetValueOrDefault(Register.RAX); floatReturned = registers.GetValueOrDefault(Register.XMM0.GetFullRegister());
                    break;
                }
                continue;
            }
            foreach (UsedRegister register in information.GetInfo(instruction).GetUsedRegisters())
                if (register.Access is OpAccess.Write or OpAccess.CondWrite or OpAccess.ReadWrite or OpAccess.ReadCondWrite) registers.Remove(register.Register.GetFullRegister());
        }
        return new(entry, calls.ToArray(), loads.ToArray(), stores.ToArray(), returned, floatReturned);
    }

    private NativeValue AddressOf(in Instruction instruction)
    {
        if (instruction.IsIPRelativeMemoryOperand) return new("global", unchecked((long)instruction.IPRelativeMemoryAddress));
        NativeValue? basis = registers.GetValueOrDefault(instruction.MemoryBase.GetFullRegister());
        long offset = unchecked((long)instruction.MemoryDisplacement64);
        return instruction.MemoryIndex == Register.None ? basis?.At(offset) ?? new("address", offset)
            : new("indexed", offset, Base: basis, Index: registers.GetValueOrDefault(instruction.MemoryIndex.GetFullRegister()), Scale: instruction.MemoryIndexScale);
    }

    private NativeValue? ReadOperand(in Instruction instruction, int operand)
    {
        OpKind kind = instruction.GetOpKind(operand);
        if (kind == OpKind.Register) return registers.GetValueOrDefault(instruction.GetOpRegister(operand).GetFullRegister());
        if (kind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64) return NativeValue.Constant(unchecked((long)instruction.GetImmediate(operand)));
        if (kind != OpKind.Memory) return null;
        NativeValue location = AddressOf(instruction);
        loads.Add(new(instruction.IP, location, null, instruction.MemorySize.GetSize()));
        if (location.Kind == "global" && resolveGlobal?.Invoke(unchecked((ulong)location.Number)) is NativeValue symbol)
            return symbol;
        return memory.GetValueOrDefault(location, location.Load());
    }

    private void Set(Register destination, NativeValue? value)
    {
        if (value is null) registers.Remove(destination);
        else registers[destination] = value;
    }
}
