using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class ApplyNameTranslation : Task
{
	[Required] public string Dump { get; set; }
	[Required] public string Names { get; set; }
	[Required] public string Output { get; set; }

	private static readonly Regex Identifier = new Regex(@"^[a-zA-Z_][a-zA-Z_0-9]*$");
	private static readonly Regex Declaration = new Regex(@"^(\s*)(message|enum|oneof)\s+([a-zA-Z_][a-zA-Z_0-9]*)\s*\{\s*$");
	private static readonly Regex Member = new Regex(@"\b([a-zA-Z_][a-zA-Z_0-9]*)\s*=\s*-?\d+\s*(\[[^\]]*\])?\s*;\s*$");

	public override bool Execute()
	{
		try
		{
			Dictionary<string, string> names = ReadNames();
			string translated = Apply(File.ReadLines(Dump), names);
			Directory.CreateDirectory(Path.GetDirectoryName(Output));
			File.WriteAllText(Output, translated, new UTF8Encoding(false));
			return true;
		}
		catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
		{
			Log.LogError(ex.Message);
			return false;
		}
	}

	private Dictionary<string, string> ReadNames()
	{
		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		int number = 0;
		foreach (string line in File.ReadLines(Names))
		{
			number++;
			string value = line.Trim();
			if (value.Length == 0 || value[0] == ';' || value[0] == '#' || value.StartsWith("//", StringComparison.Ordinal))
				continue;
			string[] pair = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			if (pair.Length != 2 || !pair[0].Split('.').All(part => Identifier.IsMatch(part)) || !Identifier.IsMatch(pair[1]))
				throw new IOException($"{Names}:{number}: expected <obfuscated identifier> <semantic identifier>.");
			if (names.TryGetValue(pair[0], out string previous) && previous != pair[1])
				throw new IOException($"{Names}:{number}: conflicting translation for {pair[0]}.");
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

	private static string Apply(IEnumerable<string> lines, Dictionary<string, string> names)
	{
		// Only the dump's declaration lines are annotated; protobuf-net still parses the schema.
		var result = new StringBuilder();
		var scopes = new List<string>();
		var kinds = new List<string>();
		foreach (string line in lines)
		{
			int commentStart = line.IndexOf("//", StringComparison.Ordinal);
			string code = commentStart < 0 ? line : line.Substring(0, commentStart);
			string comment = commentStart < 0 ? "" : line.Substring(commentStart);
			string trimmed = code.Trim();
			if (trimmed.StartsWith("syntax ", StringComparison.Ordinal))
			{
				result.AppendLine(line);
				result.AppendLine("import \"protobuf-net/protogen.proto\";");
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
				if (member.Success && Resolve(names, parent, member.Groups[1].Value) is string alias)
				{
					string option = kinds[kinds.Count - 1] == "enum" ? "enumvalopt" : "fieldopt";
					string setting = $"(.protobuf_net.{option}).name = \"{alias}\"";
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
