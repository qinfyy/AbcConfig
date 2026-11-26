using CyreneDumper.Analysis.Engine.Image;
using Iced.Intel;

namespace CyreneDumper.Analysis.Cli.Config;

internal static class RawConfigLayout
{
    public static IReadOnlyDictionary<uint, string> Read(NativeImage image, IReadOnlyDictionary<uint, ConfigRegistration> registrations)
    {
        uint type = ConfigRegistration.FindType(registrations, image.NormalizeAddress(0x0229A588));
        const ulong reader = 0x04D7ED60, lookup = 0x04F28990;
        ulong stringReader = image.NormalizeAddress(0x08245F90);
        Instruction[] code = image.DecodeFunctionBody(reader);
        Require(reader,
            code.Any(i => Load(i, Mnemonic.Mov, Register.RAX, Register.RDI, 8, 8))
            && code.Any(i => RegisterPair(i, Mnemonic.Add, Register.RAX, Register.RDI))
            && code.Any(i => Load(i, Mnemonic.Mov, Register.RBX, Register.RDI, 0x10, 8))
            && code.Any(i => RegisterPair(i, Mnemonic.Add, Register.RBX, Register.RDI))
            && code.Any(i => Load(i, Mnemonic.Movsxd, Register.R14, Register.RDI, 0x18, 4))
            && code.Any(i => AddImmediate(i, Register.RBX, 0x30))
            && code.Any(i => Load(i, Mnemonic.Mov, Register.R8D, Register.RBX, 0, 4, Register.RAX))
            && code.Any(i => AddImmediate(i, Register.RCX, 4))
            && code.Any(i => i.Mnemonic == Mnemonic.Call && i.Op0Kind == OpKind.NearBranch64 && i.NearBranchTarget == stringReader));

        Require(lookup, image.DecodeFunctionBody(lookup).Any(i => i.Mnemonic == Mnemonic.Cmp && i.OpCount == 2
            && i.GetOpKind(1) == OpKind.Register && i.Op1Register == Register.EDX
            && Memory(i, 0, Register.R9, 0, 4, Register.RAX, 4)));

        (ulong Rva, ulong Offset)[] getters = [(0x04FEDF80, 8), (0x04FEE020, 0x14), (0x04FEDE90, 0x20)];
        foreach ((ulong rva, ulong offset) in getters)
            Require(rva, image.DecodeFunctionBody(rva).Any(i => Load(i, Mnemonic.Mov, Register.RDX, Register.RAX, offset, 8)));
        return new Dictionary<uint, string> { [type] = "uint32-vector-table-v1" };
    }

    private static void Require(ulong rva, bool verified)
    {
        if (!verified)
            throw new InvalidDataException($"Raw Config reader layout could not be verified at RVA 0x{rva:X}.");
    }

    private static bool Memory(Instruction i, int operand, Register basis, ulong offset, int size, Register index = Register.None, int scale = 1) =>
        i.GetOpKind(operand) == OpKind.Memory && i.MemoryBase == basis && i.MemoryIndex == index
            && (index == Register.None || i.MemoryIndexScale == scale) && i.MemoryDisplacement64 == offset && i.MemorySize.GetSize() == size;

    private static bool Load(Instruction i, Mnemonic mnemonic, Register target, Register basis, ulong offset, int size, Register index = Register.None) =>
        i.Mnemonic == mnemonic && i.OpCount == 2 && i.Op0Kind == OpKind.Register && i.Op0Register == target
            && Memory(i, 1, basis, offset, size, index);

    private static bool RegisterPair(Instruction i, Mnemonic mnemonic, Register first, Register second) =>
        i.Mnemonic == mnemonic && i.OpCount == 2 && i.Op0Kind == OpKind.Register && i.Op0Register == first
            && i.Op1Kind == OpKind.Register && i.Op1Register == second;

    private static bool AddImmediate(Instruction i, Register target, ulong value) =>
        i.Mnemonic == Mnemonic.Add && i.OpCount == 2 && i.Op0Kind == OpKind.Register && i.Op0Register == target
            && i.Op1Kind is OpKind.Immediate8to64 or OpKind.Immediate32to64 && i.GetImmediate(1) == value;
}
