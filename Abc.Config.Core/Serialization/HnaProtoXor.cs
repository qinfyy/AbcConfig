using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using AbcDumper.Proto;
using ProtoBuf;

namespace Abc.Config.Core.Serialization;

internal sealed class HnaProtoXor
{
	private readonly Dictionary<Type, Dictionary<int, XorField>> _keys = [];
	private readonly Dictionary<Type, Dictionary<int, ProtoField>> _children = [];

	public HnaProtoXor(IReadOnlyDictionary<string, Type> contracts)
	{
		Regex scope = new(@"^\s*(message|enum|oneof)\s+(\w+)\s*\{");
		Regex xor = new(@"^\s*(?:(repeated)\s+)?uint32\s+\w+\s*=\s*(\d+).*?//.*?\bXOR: 0x([0-9A-Fa-f]+)\b");
		using StreamReader input = new(SchemaSource.Open());
		List<string> names = [];
		while (input.ReadLine() is string line)
		{
			Match start = scope.Match(line);
			if (start.Success)
				names.Add(start.Groups[1].Value == "message" ? start.Groups[2].Value : "");
			else if (line.TrimStart().StartsWith('}') && names.Count != 0)
				names.RemoveAt(names.Count - 1);
			if (!line.Contains("XOR:", StringComparison.Ordinal))
				continue;
			Match field = xor.Match(line);
			if (!field.Success || !contracts.TryGetValue(string.Join('.', names.Where(name => name.Length != 0)), out Type? type))
				throw new InvalidDataException($"Unsupported protobuf XOR declaration: {line.Trim()}");
			if (!_keys.TryGetValue(type, out var keys))
				_keys.Add(type, keys = []);
			keys.Add(int.Parse(field.Groups[2].Value, CultureInfo.InvariantCulture),
				new(uint.Parse(field.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), field.Groups[1].Success));
		}

		// Limit wire normalization to messages that can contain an annotated field.
		var parents = contracts.Values.SelectMany(type => HnaProtoCompatibility.GetFields(type)
				.Where(field => field.MessageType is not null).Select(field => (Type: type, Field: field)))
			.ToLookup(member => member.Field.MessageType!);
		Queue<Type> remaining = new(_keys.Keys);
		HashSet<Type> visited = [.. _keys.Keys];
		while (remaining.TryDequeue(out Type? child))
		{
			foreach ((Type type, ProtoField field) in parents[child])
			{
				if (!_children.TryGetValue(type, out var fields))
					_children.Add(type, fields = []);
				fields.Add(field.Tag, field);
				if (visited.Add(type))
					remaining.Enqueue(type);
			}
		}
	}

	public ReadOnlyMemory<byte> Decode(Type type, ReadOnlyMemory<byte> bytes) =>
		_keys.ContainsKey(type) || _children.ContainsKey(type) ? Rewrite(bytes, type, false, 0) : bytes;

	private ReadOnlyMemory<byte> Rewrite(ReadOnlyMemory<byte> bytes, Type type, bool mapEntry, int depth)
	{
		if (depth == 64)
			throw new InvalidDataException("Protobuf XOR message nesting exceeds 64 levels.");
		if (bytes.IsEmpty)
			return bytes;
		var keys = mapEntry ? null : _keys.GetValueOrDefault(type);
		var children = mapEntry ? null : _children.GetValueOrDefault(type);
		ArrayBufferWriter<byte> buffer = new(bytes.Length);
		ProtoReader.State reader = ProtoReader.State.Create(bytes, null, null);
		try
		{
			int start = 0, tag;
			while ((tag = reader.ReadFieldHeader()) > 0)
			{
				if (keys?.TryGetValue(tag, out XorField xor) == true && reader.WireType == WireType.Varint)
					WriteValue(buffer, tag, reader.ReadUInt32() ^ xor.Key);
				else if (keys?.TryGetValue(tag, out xor) == true && xor.Repeated && reader.WireType == WireType.String)
				{
					SubItemToken packed = reader.StartSubItem();
					while (reader.HasSubValue(WireType.Varint))
						WriteValue(buffer, tag, reader.ReadUInt32() ^ xor.Key);
					reader.EndSubItem(packed);
				}
				else if (reader.WireType == WireType.String && (mapEntry && tag == 2 || children?.ContainsKey(tag) == true))
				{
					ProtoField? field = children?.GetValueOrDefault(tag);
					var payload = reader.AppendBytes(default(ReadOnlyMemory<byte>));
					WriteMessage(buffer, tag, Rewrite(payload, field?.MessageType ?? type, field?.IsMap ?? false, depth + 1));
				}
				else
				{
					reader.SkipField();
					buffer.Write(bytes.Span[start..checked((int)reader.GetPosition())]);
				}
				start = checked((int)reader.GetPosition());
			}
			if (start != bytes.Length || reader.WireType == WireType.EndGroup)
				throw new InvalidDataException("Protobuf XOR data did not end at the message boundary.");
			return buffer.WrittenMemory;
		}
		finally { reader.Dispose(); }
	}

	private static void WriteValue(ArrayBufferWriter<byte> buffer, int tag, uint value)
	{
		using ProtoWriter.State writer = ProtoWriter.State.Create(buffer, null, null);
		writer.WriteFieldHeader(tag, WireType.Varint);
		writer.WriteUInt32(value);
		writer.Flush();
	}

	private static void WriteMessage(ArrayBufferWriter<byte> buffer, int tag, ReadOnlyMemory<byte> value)
	{
		using ProtoWriter.State writer = ProtoWriter.State.Create(buffer, null, null);
		writer.WriteFieldHeader(tag, WireType.String);
		writer.WriteBytes(value);
		writer.Flush();
	}

	private readonly record struct XorField(uint Key, bool Repeated);
}
