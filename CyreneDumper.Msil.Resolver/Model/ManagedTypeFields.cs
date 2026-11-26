using CyreneDumper.Msil.Resolver.Input;

namespace CyreneDumper.Msil.Resolver.Model;

public sealed class ManagedTypeFields(IReadOnlyDictionary<string, ManagedType> types)
{
    public IEnumerable<ManagedField> Read(ManagedType owner)
    {
        Dictionary<string, string> arguments = [];
        HashSet<string> visited = [];
        while (visited.Add(owner.Name))
        {
            foreach (ManagedField field in owner.Fields) yield return field with { Type = Substitute(field.Type, arguments) };
            if (owner.Base is not string parent) yield break;
            var inherited = ReadTypeName(Substitute(parent, arguments));
            if (!types.TryGetValue(MetadataCatalog.Normalize(inherited.Name), out owner!)) yield break;
            string[] parameters = owner.GenericParameters ?? [];
            if (parameters.Length != inherited.Arguments.Length) yield break;
            arguments = parameters.Zip(inherited.Arguments).ToDictionary(pair => pair.First, pair => pair.Second);
        }
        throw new InvalidDataException("资源引用类型存在继承循环。");
    }

    private static string Substitute(string name, IReadOnlyDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue(name, out string? value)) return value;
        if (name.EndsWith("[]", StringComparison.Ordinal)) return Substitute(name[..^2], arguments) + "[]";
        if (name.EndsWith('&')) return Substitute(name[..^1], arguments) + "&";
        var type = ReadTypeName(name);
        return type.Arguments.Length == 0 ? name : type.Name + "<" + string.Join(',', type.Arguments.Select(argument => Substitute(argument, arguments))) + ">";
    }

    private static (string Name, string[] Arguments) ReadTypeName(string name)
    {
        int open = name.IndexOf('<');
        if (open < 0) return (name, []);
        if (!name.EndsWith('>')) throw new InvalidDataException("无效的 metadata 泛型：" + name);
        int depth = 0, start = open + 1;
        List<string> arguments = [];
        for (int index = start; index < name.Length - 1; index++)
        {
            switch (name[index])
            {
                case '<': case '[': depth++; break;
                case '>': case ']': depth--; break;
                case ',' when depth == 0:
                    arguments.Add(name[start..index].Trim());
                    start = index + 1;
                    break;
            }
            if (depth < 0) throw new InvalidDataException("无效的 metadata 泛型：" + name);
        }
        arguments.Add(name[start..^1].Trim());
        if (depth != 0 || arguments.Any(string.IsNullOrEmpty)) throw new InvalidDataException("无效的 metadata 泛型：" + name);
        return (name[..open], arguments.ToArray());
    }
}
