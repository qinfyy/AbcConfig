using System.Buffers.Binary;
using CyreneDumper.Analysis.Engine.Image;
using Iced.Intel;

namespace CyreneDumper.Analysis.Engine.Disassembly;

public sealed record NativeSwitch(ulong Load, Register Index, ulong[] Targets);

internal sealed class RelativeJumpTableReader(PeModule module)
{
    public NativeSwitch Read(Instruction[] code, Instruction jump)
    {
        Instruction[] before = code.Where(item => item.IP <= jump.IP).TakeLast(12).ToArray();
        int last = before.Length - 1;
        if (last < 5 || jump.Op0Kind != OpKind.Register) throw new InvalidDataException("原生间接分支没有可核实跳表。");
        Instruction add = before[last - 1], load = before[last - 2], address = before[last - 3];
        if (add.Mnemonic != Mnemonic.Add || add.Op0Kind != OpKind.Register || add.Op1Kind != OpKind.Register || add.Op0Register != jump.Op0Register
            || load.Mnemonic != Mnemonic.Movsxd || load.Op0Register != jump.Op0Register || load.Op1Kind != OpKind.Memory || load.MemoryIndexScale != 4
            || load.MemoryBase != add.Op1Register || load.MemoryIndex == Register.None || load.MemoryDisplacement64 != 0
            || address.Mnemonic != Mnemonic.Lea || address.Op0Register != load.MemoryBase || !address.IsIPRelativeMemoryOperand
            || address.NextIP != load.IP || load.NextIP != add.IP || add.NextIP != jump.IP)
            throw new InvalidDataException("原生间接分支不符合已核实的相对跳表 ABI。");
        int guard = last - 4;
        if (before[guard].Mnemonic == Mnemonic.Mov && before[guard].Op0Kind == OpKind.Register && before[guard].Op1Kind == OpKind.Register
            && before[guard].Op0Register == before[guard].Op1Register && before[guard].Op0Register.GetFullRegister() == load.MemoryIndex.GetFullRegister()
            && before[guard].Op0Register.GetSize() == 4 && before[guard].NextIP == address.IP) guard--;
        if (guard < 1 || before[guard].Mnemonic != Mnemonic.Ja || before[guard - 1].Mnemonic != Mnemonic.Cmp
            || before[guard - 1].Op0Kind != OpKind.Register || before[guard - 1].Op0Register.GetFullRegister() != load.MemoryIndex.GetFullRegister()
            || before[guard - 1].Op0Register.GetSize() != 4
            || before[guard - 1].Op1Kind is not (>= OpKind.Immediate8 and <= OpKind.Immediate32to64)
            || before[guard - 1].NextIP != before[guard].IP || before[guard].NextIP != before[guard + 1].IP)
            throw new InvalidDataException("原生跳表缺少同一索引的无符号上界检查。");
        if (code.Any(item => item.FlowControl is FlowControl.ConditionalBranch or FlowControl.UnconditionalBranch
            && item.NearBranchTarget >= before[guard].NextIP && item.NearBranchTarget < jump.NextIP))
            throw new InvalidDataException("原生跳表存在绕过上界检查的分支入口。");
        ulong maximum = before[guard - 1].GetImmediate(1);
        if (maximum > 4095) throw new InvalidDataException("原生跳表范围超过分析上限。");
        ulong table = address.IPRelativeMemoryAddress;
        byte[] bytes = module.Read(table, checked(((int)maximum + 1) * 4));
        if (bytes.Length != ((int)maximum + 1) * 4) throw new InvalidDataException("原生跳表被截断。");
        ulong[] targets = Enumerable.Range(0, (int)maximum + 1).Select(index => unchecked((ulong)((long)table
            + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * 4, 4))))).ToArray();
        return new(load.IP, load.MemoryIndex.GetFullRegister(), targets);
    }
}
