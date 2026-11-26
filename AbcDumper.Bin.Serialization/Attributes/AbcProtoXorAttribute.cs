namespace AbcDumper.Bin.Serialization.Attributes;

/// <summary>Marks an encoded uint32 field (or repeated uint32 field) in the ABC wire format.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbcProtoXorAttribute : Attribute
{
	public uint Mask { get; set; }
}
