using System.Buffers.Binary;
using AbcDumper.Contracts.Model;
using CyreneDumper.Analysis.Engine.Image;
using Iced.Intel;

namespace CyreneDumper.Analysis.Cli.Config;

internal static class TextMapLayout
{
    private static readonly string[] Languages = ["CHS", "EN", "JP", "KR", "CHT", "VI", "DE", "RU", "FR", "TH", "ES", "PT", "ID"];

    public static TextMapContract Read(NativeImage image, IReadOnlyDictionary<uint, ConfigRegistration> registrations)
    {
        uint type = ConfigRegistration.FindType(registrations, image.NormalizeAddress(0x0229B508));

        // The reader dispatches the base message, text, and unsigned key.
        const int dispatch = 0x008FA9D4, tableSize = 33 * sizeof(int);
        byte[] table = image.Read(dispatch, tableSize);
        if (table.Length != tableSize || Target(10) != 0x04D9EE40 || Target(26) != 0x04D9EF17 || Target(32) != 0x04D9EF50)
            throw new InvalidDataException("TextMap entry reader tags could not be verified.");

        Instruction stride = image.Decode(0x04BE0B82, 6).FirstOrDefault();
        if (stride.Mnemonic != Mnemonic.Imul || stride.OpCount != 3 || stride.Op2Kind != OpKind.Immediate32
            || stride.Immediate32 != 100_000_000)
            throw new InvalidDataException("TextMap record bucket stride could not be verified.");

        // Bucket order comes from matching translations across the actual package.
        return new(type, stride.Immediate32, Languages.Select((name, index) => (Bucket: (uint)index + 1, name))
            .ToDictionary(pair => pair.Bucket, pair => pair.name));

        long Target(int tag) => (long)dispatch + BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(tag * sizeof(int)));
    }
}
