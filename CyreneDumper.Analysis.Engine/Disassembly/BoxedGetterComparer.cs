using CyreneDumper.Analysis.Engine.Image;
using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Disassembly;

// 合并相同方法的装箱/非装箱入口；只接受逐条指令及分支对应的 this+16 差异。
public sealed class BoxedGetterComparer(NativeImage image)
{
    public bool Equivalent(ulong boxed, ulong unboxed) => Compare(boxed, unboxed, Register.RCX) || Compare(boxed, unboxed, Register.RDX);

    public bool ForwardsTo(ulong boxed, ulong unboxed)
    {
        ForwardingFrame frame = new(image.NormalizeAddress(unboxed));
        Instruction[] instructions = image.Decode(boxed, 128);
        return instructions.Length != 0 && instructions.All(frame.Accept) && frame.Complete;
    }

    private bool Compare(ulong boxed, ulong unboxed, Register thisRegister)
    {
        Queue<BlockPair> pending = new();
        pending.Enqueue(new(boxed, unboxed, new() { [thisRegister] = -16 }, new() { [thisRegister] = 0 }));
        HashSet<(ulong, ulong)> visited = [];
        while (pending.TryDequeue(out BlockPair? pair))
        {
            if (!visited.Add((pair.Boxed, pair.Unboxed))) continue;
            if (visited.Count > 64) return false;
            Instruction[] left = image.Decode(pair.Boxed, 512), right = image.Decode(pair.Unboxed, 512);
            if (left.Length != right.Length || left.Length == 0) return false;
            for (int index = 0; index < left.Length; index++)
            {
                Instruction first = left[index], second = right[index];
                if (first.Mnemonic != second.Mnemonic || first.OpCount != second.OpCount || first.FlowControl != second.FlowControl) return false;
                for (int operand = 0; operand < first.OpCount; operand++)
                    if (!SameOperand(first, second, operand, pair.LeftAliases, pair.RightAliases)) return false;
                if (first.FlowControl == FlowControl.ConditionalBranch || first.FlowControl == FlowControl.UnconditionalBranch && first.NearBranchTarget != second.NearBranchTarget)
                    pending.Enqueue(new(first.NearBranchTarget, second.NearBranchTarget, new(pair.LeftAliases), new(pair.RightAliases)));
                UpdateAliases(first, pair.LeftAliases);
                UpdateAliases(second, pair.RightAliases);
            }
            if (left[^1].FlowControl is not (FlowControl.Return or FlowControl.UnconditionalBranch or FlowControl.Interrupt or FlowControl.Exception)) return false;
        }
        return true;
    }

    private bool SameOperand(Instruction left, Instruction right, int operand, Dictionary<Register, long> leftAliases, Dictionary<Register, long> rightAliases)
    {
        OpKind kind = left.GetOpKind(operand);
        if (kind != right.GetOpKind(operand)) return false;
        if (kind == OpKind.Register) return left.GetOpRegister(operand) == right.GetOpRegister(operand);
        if (kind == OpKind.NearBranch64)
            return left.FlowControl is FlowControl.ConditionalBranch or FlowControl.UnconditionalBranch || left.NearBranchTarget == right.NearBranchTarget;
        if (kind != OpKind.Memory) return left.GetImmediate(operand) == right.GetImmediate(operand);
        if (left.MemoryBase != right.MemoryBase || left.MemoryIndex != right.MemoryIndex || left.MemoryIndexScale != right.MemoryIndexScale || left.MemorySize != right.MemorySize) return false;
        if (left.IsIPRelativeMemoryOperand || right.IsIPRelativeMemoryOperand) return left.IPRelativeMemoryAddress == right.IPRelativeMemoryAddress;
        long leftOffset = unchecked((long)left.MemoryDisplacement64), rightOffset = unchecked((long)right.MemoryDisplacement64);
        if (leftAliases.TryGetValue(left.MemoryBase.GetFullRegister(), out long first)) leftOffset += first;
        if (rightAliases.TryGetValue(right.MemoryBase.GetFullRegister(), out long second)) rightOffset += second;
        return leftOffset == rightOffset;
    }

    private void UpdateAliases(Instruction instruction, Dictionary<Register, long> aliases)
    {
        if (instruction.FlowControl is FlowControl.Call or FlowControl.IndirectCall)
        {
            foreach (Register register in new[] { Register.RAX, Register.RCX, Register.RDX, Register.R8, Register.R9, Register.R10, Register.R11 }) aliases.Remove(register);
            return;
        }
        if (instruction.Op0Kind != OpKind.Register || !instruction.Op0Register.IsGPR()) return;
        if (instruction.Mnemonic is Mnemonic.Cmp or Mnemonic.Test or Mnemonic.Push) return;
        Register target = instruction.Op0Register.GetFullRegister();
        if (instruction.Mnemonic == Mnemonic.Mov && instruction.Op1Kind == OpKind.Register && aliases.TryGetValue(instruction.Op1Register.GetFullRegister(), out long alias))
            aliases[target] = alias;
        else aliases.Remove(target);
    }

    private sealed record BlockPair(ulong Boxed, ulong Unboxed, Dictionary<Register, long> LeftAliases, Dictionary<Register, long> RightAliases);

    // 仅允许装箱 this 调整、平衡的栈帧和结构体返回缓冲区转发，拒绝夹带其他计算的包装器。
    private sealed class ForwardingFrame(ulong target)
    {
        private readonly Stack<Register> _saved = new();
        private Register _this, _returnBuffer;
        private ulong _stack;
        private bool _called, _returnedBuffer;
        public bool Complete { get; private set; }

        public bool Accept(Instruction instruction)
        {
            if (Complete) return false;
            if (instruction.Mnemonic == Mnemonic.Nop) return true;
            if (instruction.FlowControl == FlowControl.UnconditionalBranch)
            {
                Complete = !_called && _this != Register.None && _saved.Count == 0 && _stack == 0 && _returnBuffer == Register.None && IsTarget(instruction);
                return Complete;
            }
            if (instruction.FlowControl == FlowControl.Call)
            {
                if (_called || _this == Register.None || !IsTarget(instruction) || _returnBuffer != Register.None && _this != Register.RDX) return false;
                _called = true;
                return true;
            }
            if (instruction.Mnemonic == Mnemonic.Ret)
            {
                Complete = _called && _stack == 0 && _saved.Count == 0 && instruction.OpCount == 0 && (_returnBuffer == Register.None || _returnedBuffer);
                return Complete;
            }
            if (instruction.Op0Kind != OpKind.Register) return false;
            Register register = instruction.Op0Register;
            if (instruction.Mnemonic == Mnemonic.Push && !_called && _stack == 0 && IsNonvolatile(register))
            { if (_saved.Contains(register)) return false; _saved.Push(register); return true; }
            if (instruction.Mnemonic == Mnemonic.Pop && _called)
                return _stack == 0 && _saved.TryPop(out Register saved) && saved == register;
            if (instruction.Mnemonic == Mnemonic.Mov && instruction.Op1Kind == OpKind.Register)
            {
                if (!_called && _returnBuffer == Register.None && _saved.Contains(register) && instruction.Op1Register == Register.RCX)
                { _returnBuffer = register; return true; }
                if (_called && !_returnedBuffer && register == Register.RAX && _returnBuffer != Register.None && instruction.Op1Register == _returnBuffer)
                { _returnedBuffer = true; return true; }
                return false;
            }
            if (instruction.Op1Kind is not (OpKind.Immediate8to64 or OpKind.Immediate32to64)) return false;
            ulong amount = instruction.GetImmediate(1);
            if (!_called && instruction.Mnemonic == Mnemonic.Add && register is Register.RCX or Register.RDX && amount == 16 && _this == Register.None)
            { _this = register; return true; }
            if (register != Register.RSP || amount == 0 || amount > 4096) return false;
            if (!_called && instruction.Mnemonic == Mnemonic.Sub && _stack == 0) { _stack = amount; return true; }
            if (_called && instruction.Mnemonic == Mnemonic.Add && _stack == amount) { _stack = 0; return true; }
            return false;
        }

        private bool IsTarget(Instruction instruction) => instruction.Op0Kind == OpKind.NearBranch64 && instruction.NearBranchTarget == target;
        private bool IsNonvolatile(Register register) => register is Register.RBX or Register.RSI or Register.RDI or Register.RBP or Register.R12 or Register.R13 or Register.R14 or Register.R15;
    }
}
