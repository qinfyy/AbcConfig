using System.Reflection;
using Abc.Bin.Serialization;
using AbcDumper.Bin;
using AbcDumper.Proto;
using ProtoBuf;

namespace AbcDumper.Bin.Reading;

/// <summary>Binds registered type IDs to Config contracts and reads indexed records.</summary>
public sealed class AbcConfigReader
{
	private static readonly Dictionary<string, Type> Contracts = typeof(ConfigLoadManifest).Assembly.GetTypes()
		.Where(type => type.IsClass && type.IsDefined(typeof(ProtoContractAttribute), false))
		.ToDictionary(GetSchemaName, StringComparer.Ordinal);
	private readonly ConfigPackage _package;
	private readonly IReadOnlyDictionary<uint, ConfigRecordType> _types;
	private readonly AbcProtoSerializer _serializer = new();

	public AbcConfigReader(ConfigPackage package, IReadOnlyDictionary<uint, string> schemaNames,
		IReadOnlyDictionary<uint, string>? nativeLayouts = null)
	{
		ArgumentNullException.ThrowIfNull(package);
		ArgumentNullException.ThrowIfNull(schemaNames);
		_package = package;
		_types = schemaNames.ToDictionary(pair => pair.Key,
			pair => Bind(pair.Value, nativeLayouts?.GetValueOrDefault(pair.Key)));
	}

	public ConfigRecordType? Resolve(uint typeId) => _types.GetValueOrDefault(typeId);

	public ConfigDecodeResult Read(uint typeId, uint recordId) => Read(_package.Find(typeId, recordId));

	public ConfigDecodeResult Read(ConfigRecord record)
	{
		if (Resolve(record.TypeId) is not ConfigRecordType binding)
			return new(ConfigDecodeStatus.UnmappedType);
		if (binding.NativeLayout is string layout)
			return new(ConfigDecodeStatus.NonProtobuf, Issues: [new("", "nativeLayout", layout)]);
		if (binding.Contract is not Type type)
			return new(ConfigDecodeStatus.MissingSchema);
		byte[] bytes;
		try
		{
			bytes = _package.ReadBytes(record);
		}
		catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			return new(ConfigDecodeStatus.ReadFailed, Issues: [new("", "recordReadError", error.Message)]);
		}
		try
		{
			object value = _serializer.Deserialize(type, bytes);
			ProtoUnknownField[] unknown = AbcProtoSerializer.FindUnknownFields(value,
				static (owner, tag) => owner == typeof(LocalizedTextEntry) && tag == 1);
			return unknown.Length == 0 ? new(ConfigDecodeStatus.Decoded, value)
				: new(ConfigDecodeStatus.Partial, value,
					unknown.Select(field => new DecodeIssue(field.Path, "unknownField", field.WireType.ToString())).ToArray());
		}
		catch (Exception error) when (error is ProtoException or InvalidDataException or EndOfStreamException or OverflowException)
		{
			return new(ConfigDecodeStatus.DecodeFailed, Issues: [new("", "decodeError", error.Message)]);
		}
	}

	private static ConfigRecordType Bind(string schemaName, string? nativeLayout)
	{
		Type? contract = nativeLayout is null ? Contracts.GetValueOrDefault(schemaName.TrimStart('.')) : null;
		string name = contract is not null && contract.Name != schemaName[(schemaName.LastIndexOf('.') + 1)..] ? contract.Name : schemaName;
		return new(schemaName, name, contract, nativeLayout);
	}

	/// <summary>Reads a fully decoded record and verifies its generated model type.</summary>
	public T ReadExact<T>(uint typeId, uint recordId) where T : class
	{
		if (Resolve(typeId)?.Contract != typeof(T))
			throw new ArgumentException($"Config type 0x{typeId:X8} is not bound to {typeof(T).Name}.", nameof(typeId));
		ConfigDecodeResult result = Read(typeId, recordId);
		return result.Status == ConfigDecodeStatus.Decoded && result.Data is T value ? value
			: throw new InvalidDataException($"Config record 0x{typeId:X8}:{recordId}: {result.Status}.");
	}

	private static string GetSchemaName(Type type)
	{
		string name = type.GetCustomAttribute<ProtoContractAttribute>()?.Name ?? type.Name;
		return type.DeclaringType is Type parent ? GetSchemaName(parent) + "." + name : name;
	}
}

public sealed record ConfigRecordType(string SchemaName, string Name, Type? Contract, string? NativeLayout);
