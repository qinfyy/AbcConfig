using System.Buffers;
using System.Collections.Concurrent;
using ProtoBuf;

namespace Abc.Bin.Serialization;

internal sealed class ProtoXor
{
	private readonly ConcurrentDictionary<Type, IReadOnlySet<Type>> _routes = new();

	public ReadOnlyMemory<byte> Decode(Type type, ReadOnlyMemory<byte> bytes)
	{
		var routes = _routes.GetOrAdd(type, BuildRoutes);
		return routes.Contains(type) ? Rewrite(bytes, type, routes, false, 0) : bytes;
	}

	private static HashSet<Type> BuildRoutes(Type root)
	{
		Dictionary<Type, HashSet<Type>> parents = [];
		HashSet<Type> visited = [root], routes = [];
		Queue<Type> pending = new([root]), masked = new();
		while (pending.TryDequeue(out Type? type))
		{
			foreach (ProtoField field in ProtoFields.For(type).Values)
			{
				if (field.XorMask.HasValue)
				{
					if (field.Property.PropertyType != typeof(uint) && !field.IsRepeatedUInt32)
						throw new InvalidDataException($"Invalid protobuf XOR field: {type.Name}.{field.Property.Name}.");
					if (routes.Add(type))
						masked.Enqueue(type);
				}
				if (field.MessageType is not Type child)
					continue;
				if (!parents.TryGetValue(child, out var containers))
					parents.Add(child, containers = []);
				containers.Add(type);
				if (visited.Add(child))
					pending.Enqueue(child);
			}
		}
		// Walk backwards to include recursive messages without reprocessing unrelated branches.
		while (masked.TryDequeue(out Type? child))
			if (parents.TryGetValue(child, out var containers))
				foreach (Type parent in containers)
					if (routes.Add(parent))
						masked.Enqueue(parent);
		return routes;
	}

	private static ReadOnlyMemory<byte> Rewrite(ReadOnlyMemory<byte> bytes, Type type, IReadOnlySet<Type> routes, bool mapEntry, int depth)
	{
		if (depth == 64)
			throw new InvalidDataException("Protobuf XOR message nesting exceeds 64 levels.");
		if (bytes.IsEmpty)
			return bytes;
		var fields = mapEntry ? null : ProtoFields.For(type);
		ArrayBufferWriter<byte> buffer = new(bytes.Length);
		ProtoReader.State reader = ProtoReader.State.Create(bytes, null, null);
		try
		{
			int start = 0, tag;
			while ((tag = reader.ReadFieldHeader()) > 0)
			{
				ProtoField? field = fields?.GetValueOrDefault(tag);
				if (field?.XorMask is uint mask && reader.WireType == WireType.Varint)
					WriteValue(buffer, tag, reader.ReadUInt32() ^ mask);
				else if (field?.XorMask is uint packedMask && field.IsRepeatedUInt32 && reader.WireType == WireType.String)
				{
					SubItemToken packed = reader.StartSubItem();
					while (reader.HasSubValue(WireType.Varint))
						WriteValue(buffer, tag, reader.ReadUInt32() ^ packedMask);
					reader.EndSubItem(packed);
				}
				else if (reader.WireType == WireType.String &&
					(mapEntry && tag == 2 || field?.MessageType is Type child && routes.Contains(child)))
				{
					var payload = reader.AppendBytes(default(ReadOnlyMemory<byte>));
					WriteMessage(buffer, tag, Rewrite(payload, field?.MessageType ?? type, routes, field?.IsMap ?? false, depth + 1));
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
}
