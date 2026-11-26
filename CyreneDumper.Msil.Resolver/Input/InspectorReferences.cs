using System.Globalization;
using System.Text.Json;

namespace CyreneDumper.Msil.Resolver.Input;

public sealed class InspectorReferences
{
    public Dictionary<ulong, string> Literals { get; } = [];
    public Dictionary<ulong, ulong> Methods { get; } = [];
    public Dictionary<ulong, string> MethodSignatures { get; } = [];
    public Dictionary<ulong, string> Types { get; } = [];
    public Dictionary<ulong, string> NativeTypes { get; } = [];

    public static InspectorReferences Read(string file)
    {
        InspectorReferences result = new();
        using FileStream input = File.OpenRead(file);
        byte[] buffer = new byte[1024 * 1024];
        int buffered = input.ReadAtLeast(buffer.AsSpan(0, 3), 3, throwOnEndOfStream: false);
        if (buffered == 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf) buffered = 0;
        JsonReaderState state = default;
        string? section = null, property = null, literal = null, signature = null, nativeType = null;
        ulong? address = null, method = null;
        bool inside = false, final = false;
        while (!final)
        {
            int read = input.Read(buffer.AsSpan(buffered));
            final = read == 0; buffered += read;
            Utf8JsonReader reader = new(buffer.AsSpan(0, buffered), final, state);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.CurrentDepth == 2) section = reader.GetString();
                    else if (inside && reader.CurrentDepth == 4) property = reader.GetString();
                }
                else if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 3)
                {
                    inside = section is "stringLiterals" or "methodInfoPointers" or "typeInfoPointers";
                    address = method = null; property = literal = signature = nativeType = null;
                }
                else if (inside && reader.TokenType == JsonTokenType.String && reader.CurrentDepth == 4)
                {
                    if (property is "string" or "dotNetType") literal = reader.GetString();
                    else if (property == "dotNetSignature") signature = reader.GetString();
                    else if (property == "type" && section == "typeInfoPointers") nativeType = reader.GetString();
                    else if (property is "virtualAddress" or "methodAddress")
                    {
                        string value = reader.GetString()!;
                        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            || !ulong.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong number))
                            throw new InvalidDataException("Inspector 引用地址格式错误。");
                        if (property == "virtualAddress") address = number; else method = number;
                    }
                }
                else if (inside && reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 3)
                {
                    if (address is not ulong slot) throw new InvalidDataException("Inspector 引用缺少地址。");
                    if (section == "stringLiterals" && literal is not null) Add(result.Literals, slot, literal);
                    else if (section == "typeInfoPointers")
                    {
                        if (literal is not null) Add(result.Types, slot, literal);
                        if (nativeType is not null) Add(result.NativeTypes, slot, nativeType);
                    }
                    else if (section == "methodInfoPointers" && method is ulong target)
                    {
                        Add(result.Methods, slot, target);
                        if (signature is not null) Add(result.MethodSignatures, slot, signature);
                    }
                    inside = false;
                }
            }
            int consumed = checked((int)reader.BytesConsumed);
            state = reader.CurrentState;
            buffer.AsSpan(consumed, buffered - consumed).CopyTo(buffer);
            buffered -= consumed;
            if (buffered == buffer.Length) throw new InvalidDataException("Inspector 引用的单个 token 超过读取上限。");
        }
        return result;
    }

    private static void Add<T>(Dictionary<ulong, T> values, ulong slot, T value)
    {
        if (values.TryGetValue(slot, out T? previous) && !EqualityComparer<T>.Default.Equals(previous, value))
            throw new InvalidDataException($"Inspector 引用地址存在冲突：0x{slot:x}");
        values[slot] = value;
    }
}
