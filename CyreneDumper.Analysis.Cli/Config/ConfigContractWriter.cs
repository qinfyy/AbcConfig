using System.Text.Json;
using System.Text.Json.Serialization;
using AbcDumper.Contracts.Model;
using AbcDumper.Contracts.Model.Loader;

namespace CyreneDumper.Analysis.Cli.Config;

internal static class ConfigContractWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write(Stream stream, ConfigContract contract)
    {
        var document = new
        {
            format = ConfigContractLoader.Format,
            appVersion = contract.AppVersion,
            changeList = contract.ChangeList,
            config = new
            {
                manifestSeed = contract.ManifestSeed,
                types = FormatTypes(contract.Types),
                rawCodecs = FormatTypes(contract.RawCodecs),
            },
            textMap = contract.TextMap is { } textMap
                ? new { recordType = FormatId(textMap.RecordType), recordStride = textMap.RecordStride, languages = textMap.Languages }
                : null,
        };
        JsonSerializer.Serialize(stream, document, Options);
        stream.WriteByte((byte)'\n');
    }

    private static Dictionary<string, string> FormatTypes(IReadOnlyDictionary<uint, string> types) =>
        types.OrderBy(pair => pair.Key).ToDictionary(pair => FormatId(pair.Key), pair => pair.Value);

    private static string FormatId(uint id) => $"0x{id:X8}";
}
