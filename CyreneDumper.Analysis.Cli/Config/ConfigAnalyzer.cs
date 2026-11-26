using AbcDumper.Contracts.Model;
using CyreneDumper.Analysis.Engine.Client;

namespace CyreneDumper.Analysis.Cli.Config;

internal sealed class ConfigAnalyzer(ClientAnalysisSession session)
{
    private const string AppVersion = "0.75.2";
    // The Config manifest reader references this literal from RVA 0x04C16AB2.
    private const ulong ManifestSeedLiteralRva = 0x0231F980;

    public ConfigContract Analyze(string changeList)
    {
        var image = session.Image;
        var references = session.References;
        var registrations = new ConfigRegistrationReader(image, references).Read();
        var types = registrations.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        var rawCodecs = RawConfigLayout.Read(image, registrations);
        var textMap = TextMapLayout.Read(image, registrations);
        if (!references.Literals.TryGetValue(image.NormalizeAddress(ManifestSeedLiteralRva), out string? encoded))
            throw new InvalidDataException($"Config manifest seed not found in the {AppVersion} Inspector index.");
        byte[] seed = Convert.FromBase64String(encoded);
        if (seed.Length != 32)
            throw new InvalidDataException("Expected a 32-byte Config manifest seed.");

        return new(AppVersion, changeList, seed, types, rawCodecs, textMap);
    }
}
