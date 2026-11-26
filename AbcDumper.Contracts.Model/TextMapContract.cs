namespace AbcDumper.Contracts.Model;

public sealed record TextMapContract(uint RecordType, uint RecordStride, IReadOnlyDictionary<uint, string> Languages);
