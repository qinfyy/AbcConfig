using ProtoBuf;
using ProtoBuf.Meta;

namespace AbcDumper.Bin.Serialization;

/// <summary>Decodes ABC protobuf using the wire metadata on CLR contracts.</summary>
public sealed class AbcProtoSerializer
{
	private readonly RuntimeTypeModel _model = RuntimeTypeModel.Create();
	private readonly ProtoXor _xor = new();

	public T Deserialize<T>(ReadOnlyMemory<byte> data) where T : class => (T)Deserialize(typeof(T), data);

	public object Deserialize(Type contract, ReadOnlyMemory<byte> data)
	{
		ArgumentNullException.ThrowIfNull(contract);
		if (!contract.IsClass || !contract.IsDefined(typeof(ProtoContractAttribute), false))
			throw new ArgumentException("Expected a class with ProtoContractAttribute.", nameof(contract));
		return _model.Deserialize(contract, _xor.Decode(contract, data), null, null)
			?? Activator.CreateInstance(contract)!;
	}

	/// <summary>Reports unknown wire fields retained by protobuf-net. The caller may recognize specific empty base messages.</summary>
	public static ProtoUnknownField[] FindUnknownFields(object value, Func<Type, int, bool>? allowEmptyMessage = null) =>
		ProtoFields.FindUnknown(value, allowEmptyMessage);
}

public sealed record ProtoUnknownField(string Path, WireType WireType);
