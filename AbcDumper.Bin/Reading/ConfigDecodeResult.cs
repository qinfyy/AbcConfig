namespace AbcDumper.Bin.Reading;

public enum ConfigDecodeStatus { Decoded, Partial, NonProtobuf, UnmappedType, MissingSchema, DecodeFailed, ReadFailed }

public sealed record DecodeIssue(string Path, string Code, string? Detail);
public sealed record ConfigDecodeResult(ConfigDecodeStatus Status, object? Data = null, DecodeIssue[]? Issues = null);
