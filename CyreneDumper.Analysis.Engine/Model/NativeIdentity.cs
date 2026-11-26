using System.Globalization;

namespace CyreneDumper.Analysis.Engine.Model;

public sealed record NativeFieldLayout(string[] Fields, bool Bitmap);

public static class NativeIdentity
{
    public static string Normalize(string value) => value.Replace('/', '+');
    public static ulong Address(string value) => ulong.Parse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
