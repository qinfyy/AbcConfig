using System.Globalization;
using System.Text.Json;

namespace AbcDumper.Contracts.Model.Loader;

public static class ConfigContractLoader
{
	public const string FileName = "contracts.json";
	public const string Format = "abc-config-contracts-v1";

	public static ConfigContract Load(string directory)
	{
		using Stream input = File.OpenRead(Path.Combine(directory, FileName));
		using JsonDocument document = JsonDocument.Parse(input);
		JsonElement root = document.RootElement;
		if (Get(root, "format", JsonValueKind.String).GetString() != Format)
			throw new InvalidDataException("不支持的 Config 合同格式。");
		string version = Get(root, "appVersion", JsonValueKind.String).GetString()!;
		string build = Get(root, "changeList", JsonValueKind.String).GetString()!;
		if (string.IsNullOrWhiteSpace(version) || build.Length == 0 || build.Any(value => value is < '0' or > '9'))
			throw new InvalidDataException("Config 合同的版本或 ChangeList 无效。");

		JsonElement config = Get(root, "config", JsonValueKind.Object);
		if (!Get(config, "manifestSeed", JsonValueKind.String).TryGetBytesFromBase64(out byte[]? seed) || seed.Length != 32)
			throw new InvalidDataException("Config 合同的清单种子无效。");
		Dictionary<uint, string> types = [];
		foreach (JsonProperty property in Get(config, "types", JsonValueKind.Object).EnumerateObject())
		{
			string? name = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
			if (!TryParseId(property.Name, out uint id) || string.IsNullOrWhiteSpace(name) || !types.TryAdd(id, name))
				throw new InvalidDataException($"Config 合同中的类型映射无效或重复：{property.Name}。");
		}
		if (types.Count == 0)
			throw new InvalidDataException("Config 合同缺少类型映射。");
		Dictionary<uint, string> rawCodecs = [];
		if (config.TryGetProperty("rawCodecs", out JsonElement codecs))
		{
			if (codecs.ValueKind != JsonValueKind.Object)
				throw new InvalidDataException("Config 合同的 rawCodecs 必须是对象。");
			foreach (JsonProperty property in codecs.EnumerateObject())
			{
				string? codec = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
				if (!TryParseId(property.Name, out uint id) || !types.ContainsKey(id) || string.IsNullOrWhiteSpace(codec)
					|| !rawCodecs.TryAdd(id, codec))
					throw new InvalidDataException($"Config 合同中的二进制布局无效或重复：{property.Name}。");
			}
		}
		TextMapContract? textMap = root.TryGetProperty("textMap", out JsonElement textMapElement) ? ReadTextMap(textMapElement, types) : null;
		return new(version, build, seed, types, rawCodecs, textMap);
	}

	private static TextMapContract ReadTextMap(JsonElement source, IReadOnlyDictionary<uint, string> types)
	{
		if (!TryParseId(Get(source, "recordType", JsonValueKind.String).GetString()!, out uint typeId) || !types.ContainsKey(typeId)
			|| !Get(source, "recordStride", JsonValueKind.Number).TryGetUInt32(out uint stride) || stride == 0)
			throw new InvalidDataException("TextMap 合同中的根类型或语言步长无效。");

		Dictionary<uint, string> languages = [];
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (JsonProperty property in Get(source, "languages", JsonValueKind.Object).EnumerateObject())
		{
			string? name = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
			if (!TryParseId(property.Name, out uint bucket) || bucket > uint.MaxValue / stride || name is not { Length: 2 or 3 }
				|| name.Any(character => character is < 'A' or > 'Z') || !names.Add(name) || !languages.TryAdd(bucket, name))
				throw new InvalidDataException($"TextMap 合同中的语言映射无效或重复：{property.Name}。");
		}
		if (languages.Count == 0)
			throw new InvalidDataException("TextMap 合同缺少语言映射。");
		return new(typeId, stride, languages);
	}

	public static bool TryParseId(string value, out uint id)
	{
		bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
		return uint.TryParse(hex ? value.AsSpan(2) : value.AsSpan(), hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
			CultureInfo.InvariantCulture, out id);
	}

	private static JsonElement Get(JsonElement source, string name, JsonValueKind kind) =>
		source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out JsonElement value) && value.ValueKind == kind
			? value : throw new InvalidDataException($"Config 合同缺少有效的 {name}。");
}
