using System.Reflection;
using AbcDumper.Proto;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Abc.Config.Core.Serialization;

public sealed class ConfigRecordReader
{
	private static readonly Dictionary<string, Type> Contracts = typeof(SchemaSource).Assembly.GetTypes()
		.Where(type => type.IsClass && type.IsDefined(typeof(ProtoContractAttribute), false))
		.ToDictionary(GetOriginalName, StringComparer.Ordinal);
	private static readonly Lazy<HnaProtoXor> Xor = new(() => new(Contracts));
	private readonly RuntimeTypeModel _model = RuntimeTypeModel.Create();

	public string? GetContractName(string? schemaName) => Find(schemaName)?.Name;

	public ConfigDecodeResult Read(ReadOnlyMemory<byte> bytes, string? schemaName)
	{
		if (Find(schemaName) is not Type contract)
			return new(schemaName is null ? ConfigDecodeStatus.UnmappedType : ConfigDecodeStatus.MissingSchema);
		try
		{
			object value = _model.Deserialize(contract, Xor.Value.Decode(contract, bytes), null, null)
				?? Activator.CreateInstance(contract)!;
			HashSet<DecodeIssue> issues = [];
			HnaProtoCompatibility.Inspect(value, issues);
			return issues.Count == 0 ? new(ConfigDecodeStatus.Decoded, value)
				: new(ConfigDecodeStatus.Partial, value, issues.ToArray());
		}
		catch (Exception error) when (error is ProtoException or InvalidDataException or EndOfStreamException or OverflowException)
		{
			return new(ConfigDecodeStatus.DecodeFailed, Issues: [new("", "decodeError", error.Message)]);
		}
	}

	private static Type? Find(string? name) => name is null ? null : Contracts.GetValueOrDefault(name.TrimStart('.'));

	internal static string GetOriginalName(Type type)
	{
		string name = type.GetCustomAttribute<ProtoContractAttribute>()?.Name ?? type.Name;
		return type.DeclaringType is Type parent ? GetOriginalName(parent) + "." + name : name;
	}
}
