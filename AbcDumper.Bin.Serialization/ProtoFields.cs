using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using AbcDumper.Bin.Serialization.Attributes;
using ProtoBuf;

namespace AbcDumper.Bin.Serialization;

internal static class ProtoFields
{
	private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<int, ProtoField>> FieldsByType = new();

	public static IReadOnlyDictionary<int, ProtoField> For(Type type) => FieldsByType.GetOrAdd(type, static type => type.GetProperties()
		.Select(property => (Property: property, Member: property.GetCustomAttribute<ProtoMemberAttribute>()))
		.Where(field => field.Member is not null)
		.ToDictionary(field => field.Member!.Tag, field => new ProtoField(field.Property)));

	public static ProtoUnknownField[] FindUnknown(object value, Func<Type, int, bool>? allowEmptyMessage)
	{
		HashSet<ProtoUnknownField> issues = [];
		Inspect(value, issues, allowEmptyMessage, "");
		return [.. issues];
	}

	private static void Inspect(object value, HashSet<ProtoUnknownField> issues, Func<Type, int, bool>? allowEmptyMessage, string path)
	{
		if (value is IExtensible extensible && extensible.GetExtensionObject(false) is { } extension && extension.GetLength() != 0)
		{
			Stream stream = extension.BeginQuery();
			try
			{
				ProtoReader.State reader = ProtoReader.State.Create(stream, null, null, extension.GetLength());
				try
				{
					HashSet<int>? allowedTags = null;
					int tag;
					while ((tag = reader.ReadFieldHeader()) > 0)
					{
						WireType wire = reader.WireType;
						if (wire == WireType.String && allowEmptyMessage?.Invoke(value.GetType(), tag) == true
							&& (allowedTags ??= []).Add(tag))
						{
							if (reader.AppendBytes(default(ReadOnlyMemory<byte>)).IsEmpty)
								continue;
						}
						else
							reader.SkipField();
						issues.Add(new($"{path}#{tag}", wire));
					}
				}
				finally { reader.Dispose(); }
			}
			finally { extension.EndQuery(stream); }
		}

		foreach (ProtoField field in For(value.GetType()).Values)
		{
			if (field.MessageType is null || field.Property.GetValue(value) is not object member)
				continue;
			string childPath = path + field.Property.Name + ".";
			switch (member)
			{
				case IDictionary map:
					foreach (object? child in map.Values)
						if (child is not null)
							Inspect(child, issues, allowEmptyMessage, childPath);
					break;
				case IList items:
					foreach (object? child in items)
						if (child is not null)
							Inspect(child, issues, allowEmptyMessage, childPath);
					break;
				default:
					Inspect(member, issues, allowEmptyMessage, childPath);
					break;
			}
		}
	}
}

internal sealed class ProtoField(PropertyInfo property)
{
	public PropertyInfo Property { get; } = property;
	public bool IsMap { get; } = typeof(IDictionary).IsAssignableFrom(property.PropertyType);
	public Type? MessageType { get; } = GetMessageType(property.PropertyType);
	public uint? XorMask { get; } = property.GetCustomAttribute<AbcProtoXorAttribute>()?.Mask;
	public bool IsRepeatedUInt32 => typeof(IEnumerable<uint>).IsAssignableFrom(Property.PropertyType);

	private static Type? GetMessageType(Type type)
	{
		if (type.IsArray)
			type = type.GetElementType()!;
		else if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
			type = type.GenericTypeArguments[^1];
		return type.IsClass && type.IsDefined(typeof(ProtoContractAttribute), false) ? type : null;
	}
}
