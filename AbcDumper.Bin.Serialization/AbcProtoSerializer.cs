using System.Buffers;
using ProtoBuf;
using ProtoBuf.Meta;

namespace AbcDumper.Bin.Serialization;

/// <summary>根据 CLR 合同上的 wire 元数据读写 ABC protobuf。</summary>
public sealed class AbcProtoSerializer
{
	/// <summary>使用默认 ABC wire 规则的共享实例。</summary>
	public static AbcProtoSerializer Default { get; } = new();

	private readonly RuntimeTypeModel _model = RuntimeTypeModel.Create();
	private readonly ProtoXor _xor = new();

	/// <summary>将带有 ProtoContractAttribute 的对象编码为 ABC protobuf，返回独立的字节数组。</summary>
	/// <param name="value">待编码的合同对象，使用其运行时类型。</param>
	/// <returns>已应用字段 XOR 特征的 wire 数据；未出现的字段不会被补写。</returns>
	/// <exception cref="ArgumentNullException">对象为空。</exception>
	/// <exception cref="ArgumentException">对象类型不是 protobuf 合同类。</exception>
	public byte[] Serialize(object value)
	{
		ArgumentNullException.ThrowIfNull(value);
		Type contract = value.GetType();
		ValidateContract(contract);
		ArrayBufferWriter<byte> buffer = new();
		_model.Serialize(buffer, value);
		return _xor.Transform(contract, buffer.WrittenMemory).ToArray();
	}

	public T Deserialize<T>(ReadOnlyMemory<byte> data) where T : class => (T)Deserialize(typeof(T), data);

	public object Deserialize(Type contract, ReadOnlyMemory<byte> data)
	{
		ArgumentNullException.ThrowIfNull(contract);
		ValidateContract(contract);
		return _model.Deserialize(contract, _xor.Transform(contract, data), null, null)
			?? Activator.CreateInstance(contract)!;
	}

	private static void ValidateContract(Type contract)
	{
		if (!contract.IsClass || !contract.IsDefined(typeof(ProtoContractAttribute), false))
			throw new ArgumentException("Expected a class with ProtoContractAttribute.", nameof(contract));
	}

	/// <summary>Reports unknown wire fields retained by protobuf-net. The caller may recognize specific empty base messages.</summary>
	public static ProtoUnknownField[] FindUnknownFields(object value, Func<Type, int, bool>? allowEmptyMessage = null) =>
		ProtoFields.FindUnknown(value, allowEmptyMessage);
}

public sealed record ProtoUnknownField(string Path, WireType WireType);
