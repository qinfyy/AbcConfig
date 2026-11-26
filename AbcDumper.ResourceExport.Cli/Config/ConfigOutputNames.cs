using System.Text.RegularExpressions;
using Abc.Config.Parser;

namespace AbcDumper.ResourceExport.Cli.Config;

internal static class ConfigOutputNames
{
	private static readonly Regex QualifiedName = new(@"\A[A-Za-z_][A-Za-z_0-9]*(?:\.[A-Za-z_][A-Za-z_0-9]*)*\z");
	private static readonly Regex DeviceName = new(@"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	public static Dictionary<uint, string> Resolve(IEnumerable<(uint Id, ConfigRecordType Type)> types)
	{
		var named = types.Where(entry => entry.Type.SchemaName is not null).ToArray();
		Dictionary<string, uint> originalNames = new(StringComparer.OrdinalIgnoreCase);
		foreach ((uint id, ConfigRecordType type) in named)
		{
			string original = type.SchemaName!;
			if (!IsUsable(original))
				throw new InvalidDataException($"Config 类型名不能用作文件名：{original}。");
			if (!originalNames.TryAdd(original, id))
				throw new InvalidDataException($"Config 类型文件名冲突：{original}。");
		}

		var sharedAliases = named.GroupBy(entry => entry.Type.Name!, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
		Dictionary<uint, string> names = [];
		foreach ((uint id, ConfigRecordType type) in named)
		{
			string preferred = type.Name!;
			bool useAlias = IsUsable(preferred) && !sharedAliases.Contains(preferred) &&
				(!originalNames.TryGetValue(preferred, out uint owner) || owner == id);
			names.Add(id, useAlias ? preferred : type.SchemaName!);
		}
		return names;
	}

	private static bool IsUsable(string name) => QualifiedName.IsMatch(name) && !DeviceName.IsMatch(name);
}
