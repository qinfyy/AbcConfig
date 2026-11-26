using CyreneDumper.Analysis.Engine.Image;
using CyreneDumper.Msil.Resolver.Input;
using Iced.Intel;

namespace CyreneDumper.Analysis.Cli.Config;

internal readonly record struct ConfigRegistration(ulong TypeSlot, string Name)
{
    public static uint FindType(IReadOnlyDictionary<uint, ConfigRegistration> registrations, ulong slot)
    {
        uint[] keys = registrations.Where(pair => pair.Value.TypeSlot == slot).Select(pair => pair.Key).ToArray();
        if (keys.Length != 1)
            throw new InvalidDataException($"Expected one Config registration for type slot 0x{slot:X}.");
        return keys[0];
    }
}

internal sealed class ConfigRegistrationReader(NativeImage image, InspectorReferences references)
{
    private const ulong DictionaryInsert = 0x07C3DDF0;
    private const ulong SingletonRegister = 0x049E8B40;
    private static readonly (ulong Rva, int Count)[] RegistrationFunctions = [(0x04DCFD00, 603), (0x04DDADA0, 1)];

    public IReadOnlyDictionary<uint, ConfigRegistration> Read()
    {
        SortedDictionary<uint, ConfigRegistration> registrations = [];
        ulong dictionaryInsert = image.NormalizeAddress(DictionaryInsert), singletonRegister = image.NormalizeAddress(SingletonRegister);
        foreach ((ulong rva, int expected) in RegistrationFunctions)
        {
            Instruction[] instructions = image.DecodeFunctionBody(rva);
            int count = 0;
            ulong? slot = null;
            uint? type = null;
            for (int index = 0; index < instructions.Length; index++)
            {
                Instruction instruction = instructions[index];
                if (MovesTo(instruction, Register.RSI))
                    slot = IsRipLoad(instruction) ? instruction.IPRelativeMemoryAddress : null;
                else if (MovesTo(instruction, Register.R8D) && instruction.Op1Kind == OpKind.Immediate32)
                    type = instruction.Immediate32;

                if (instruction.Mnemonic != Mnemonic.Call || instruction.Op0Kind != OpKind.NearBranch64)
                    continue;
                ulong target = instruction.NearBranchTarget;
                if (target == dictionaryInsert)
                {
                    Add(instruction.IP, slot, type);
                    count++;
                    slot = null;
                    type = null;
                }
                else if (target == singletonRegister)
                {
                    if (index < 3)
                        throw new InvalidDataException($"Incomplete singleton registration at 0x{instruction.IP:X}.");
                    Instruction load = instructions[index - 3], key = instructions[index - 2], flag = instructions[index - 1];
                    if (!MovesTo(load, Register.RCX) || !IsRipLoad(load) || !MovesTo(key, Register.EDX)
                        || key.Op1Kind != OpKind.Immediate32 || !MovesTo(flag, Register.R8L))
                        throw new InvalidDataException($"Unexpected singleton registration at 0x{instruction.IP:X}.");
                    Add(instruction.IP, load.IPRelativeMemoryAddress, key.Immediate32);
                    count++;
                }
            }
            if (count != expected)
                throw new InvalidDataException($"RVA 0x{rva:X}: found {count} Config registrations, expected {expected}.");
        }
        return registrations;

        void Add(ulong site, ulong? slot, uint? type)
        {
            const string prefix = "struct ", suffix = "__Class *";
            if (slot is not ulong address || type is not uint key || !references.NativeTypes.TryGetValue(address, out string? name)
                || name.Length <= prefix.Length + suffix.Length || !name.StartsWith(prefix, StringComparison.Ordinal)
                || !name.EndsWith(suffix, StringComparison.Ordinal)
                || !registrations.TryAdd(key, new(address, name[prefix.Length..^suffix.Length])))
                throw new InvalidDataException($"Unrecognized or duplicate Config registration at 0x{site:X}.");
        }
    }

    private static bool MovesTo(Instruction instruction, Register destination) =>
        instruction.Mnemonic == Mnemonic.Mov && instruction.OpCount == 2 && instruction.Op0Kind == OpKind.Register
            && instruction.Op0Register == destination;

    private static bool IsRipLoad(Instruction instruction) => instruction.Op1Kind == OpKind.Memory && instruction.MemoryBase == Register.RIP;
}
