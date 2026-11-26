using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using AbcDumper.Proto;
using ProtoBuf;

namespace Abc.Config.Core.Serialization;

internal static class HnaProtoCompatibility
{
	private static readonly ConcurrentDictionary<Type, ProtoField[]> FieldsByType = new();

	internal static ProtoField[] GetFields(Type type) => FieldsByType.GetOrAdd(type, static type => type.GetProperties()
		.Select(property => (Property: property, Member: property.GetCustomAttribute<ProtoMemberAttribute>()))
		.Where(field => field.Member is not null)
		.Select(field => new ProtoField(field.Member!.Tag, field.Property)).ToArray());

	public static void Inspect(object value, HashSet<DecodeIssue> issues) => Inspect(value, issues, "");

	private static void Inspect(object value, HashSet<DecodeIssue> issues, string path)
	{
		if (value is IExtensible extensible && extensible.GetExtensionObject(false) is { } extension && extension.GetLength() != 0)
		{
			Stream stream = extension.BeginQuery();
			try
			{
				ProtoReader.State reader = ProtoReader.State.Create(stream, null, null, extension.GetLength());
				try
				{
					bool emptyBaseSeen = false;
					int tag;
					while ((tag = reader.ReadFieldHeader()) > 0)
					{
						WireType wire = reader.WireType;
						// The dumped TextMap entry omits the empty HNA base message at tag 1.
						if (!emptyBaseSeen && value is LocalizedTextEntry && tag == 1 && wire == WireType.String)
						{
							emptyBaseSeen = true;
							if (reader.AppendBytes(default(ReadOnlyMemory<byte>)).IsEmpty)
								continue;
						}
						else
							reader.SkipField();
						issues.Add(new($"{path}#{tag}", "unknownField", wire.ToString()));
					}
				}
				finally { reader.Dispose(); }
			}
			finally { extension.EndQuery(stream); }
		}

		foreach (ProtoField field in GetFields(value.GetType()))
		{
			if (field.MessageType is null || field.Property.GetValue(value) is not object member)
				continue;
			string childPath = path + field.Property.Name + ".";
			switch (member)
			{
				case IDictionary map:
					foreach (object? child in map.Values)
						if (child is not null)
							Inspect(child, issues, childPath);
					break;
				case IList items:
					foreach (object? child in items)
						if (child is not null)
							Inspect(child, issues, childPath);
					break;
				default:
					Inspect(member, issues, childPath);
					break;
			}
		}
	}
}

internal sealed class ProtoField(int tag, PropertyInfo property)
{
	public int Tag { get; } = tag;
	public PropertyInfo Property { get; } = property;
	public bool IsMap { get; } = typeof(IDictionary).IsAssignableFrom(property.PropertyType);
	public Type? MessageType { get; } = GetMessageType(property.PropertyType);

	private static Type? GetMessageType(Type type)
	{
		if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
			type = type.GenericTypeArguments[^1];
		return type.IsClass && type.IsDefined(typeof(ProtoContractAttribute), false) ? type : null;
	}
}
