using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AbcDumper.Proto.Build;

internal static class ProtoSource
{
	private static readonly Regex Identifier = new Regex(@"^[a-zA-Z_][a-zA-Z_0-9]*$");
	private static readonly Regex Declaration = new Regex(@"^(\s*)(message|enum|oneof)\s+([a-zA-Z_][a-zA-Z_0-9]*)\s*\{\s*$");
	private static readonly Regex Member = new Regex(@"\b([a-zA-Z_][a-zA-Z_0-9]*)\s*=\s*-?\d+\s*(\[[^\]]*\])?\s*;\s*$");

	private static readonly Regex Xor = new(@"\bXOR:\s+0x([0-9A-Fa-f]+)\b");
	private static readonly Regex XorField = new(@"^\s*(?:repeated\s+)?uint32\s");

	public static string Read(string dump, string names) => Apply(File.ReadLines(dump), ReadNames(names), dump);

	private static Dictionary<string, string> ReadNames(string path)
	{
		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		int number = 0;
		foreach (string line in File.ReadLines(path))
		{
			number++;
			int comment = line.IndexOf(';');
			string value = (comment < 0 ? line : line[..comment]).Trim();
			if (value.Length == 0)
				continue;
			string[] pair = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			if (pair.Length != 2 || !pair[0].Split('.').All(part => Identifier.IsMatch(part)) || !Identifier.IsMatch(pair[1]))
				throw new InvalidDataException($"{path}:{number}: expected <obfuscated identifier> <semantic identifier>.");
			if (names.TryGetValue(pair[0], out string previous) && previous != pair[1])
				throw new InvalidDataException($"{path}:{number}: conflicting translation for {pair[0]}.");
			names[pair[0]] = pair[1];
		}
		return names;
	}

	private static string Resolve(Dictionary<string, string> names, string scope, string symbol)
	{
		if (scope.Length != 0 && names.TryGetValue(scope + "." + symbol, out string scoped))
			return scoped != symbol ? scoped : null;
		return names.TryGetValue(symbol, out string global) && global != symbol ? global : null;
	}

	private static string Apply(IEnumerable<string> lines, Dictionary<string, string> names, string path)
	{
		// Only the dump's declaration lines are annotated; protobuf-net still parses the schema.
		var result = new StringBuilder();
		var scopes = new List<string>();
		var kinds = new List<string>();
		int lineNumber = 0;
		foreach (string line in lines)
		{
			lineNumber++;
			int commentStart = line.IndexOf("//", StringComparison.Ordinal);
			string code = commentStart < 0 ? line : line.Substring(0, commentStart);
			string comment = commentStart < 0 ? "" : line.Substring(commentStart);
			string trimmed = code.Trim();
			if (trimmed.StartsWith("syntax ", StringComparison.Ordinal))
			{
				result.AppendLine(line);
				result.AppendLine("import \"protobuf-net/protogen.proto\";");
				result.AppendLine("import \"abc_options.proto\";");
				result.AppendLine("option csharp_namespace = \"AbcDumper.Proto\";");
				continue;
			}
			Match declaration = Declaration.Match(code);
			string parent = string.Join(".", scopes.Where(scope => scope.Length != 0));
			if (declaration.Success)
			{
				string kind = declaration.Groups[2].Value;
				string name = declaration.Groups[3].Value;
				string option = kind == "message" ? "msgopt" : kind == "enum" ? "enumopt" : "oneofopt";
				string alias = Resolve(names, parent, name);
				result.AppendLine(line);
				if (alias != null)
					result.AppendLine($"{declaration.Groups[1].Value}    option (.protobuf_net.{option}).name = \"{alias}\";");
				kinds.Add(kind);
				scopes.Add(kind == "oneof" ? "" : name);
				continue;
			}
			if (trimmed == "}" && scopes.Count > 0)
			{
				scopes.RemoveAt(scopes.Count - 1);
				kinds.RemoveAt(kinds.Count - 1);
			}
			else if (scopes.Count > 0)
			{
				Match member = Member.Match(code);
				List<string> settings = [];
				if (member.Success && Resolve(names, parent, member.Groups[1].Value) is string alias)
				{
					string option = kinds[kinds.Count - 1] == "enum" ? "enumvalopt" : "fieldopt";
					settings.Add($"(.protobuf_net.{option}).name = \"{alias}\"");
				}
				if (comment.Contains("XOR:", StringComparison.Ordinal))
				{
					Match mask = Xor.Match(comment);
					if (!member.Success || !XorField.IsMatch(code) || !mask.Success ||
						!uint.TryParse(mask.Groups[1].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
						throw new InvalidDataException($"{path}:{lineNumber}: unsupported protobuf XOR declaration.");
					settings.Add($"(.abc.xor_mask) = {value.ToString(CultureInfo.InvariantCulture)}");
				}
				if (settings.Count != 0)
				{
					string setting = string.Join(", ", settings);
					Group options = member.Groups[2];
					code = options.Success ? code.Insert(options.Index + options.Length - 1, ", " + setting)
						: code.Insert(code.LastIndexOf(';'), " [" + setting + "]");
				}
			}
			result.Append(code).AppendLine(comment);
		}
		return result.ToString();
	}
}
