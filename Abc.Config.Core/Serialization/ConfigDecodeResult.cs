using System.Text.Json.Serialization;

namespace Abc.Config.Core.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<ConfigDecodeStatus>))]
public enum ConfigDecodeStatus { Decoded, Partial, NonProtobuf, UnmappedType, MissingSchema, DecodeFailed, ReadFailed, UnmappedLanguage }

public sealed record DecodeIssue(string Path, string Code, string? Detail);
public sealed record ConfigDecodeResult(ConfigDecodeStatus Status, object? Data = null, DecodeIssue[]? Issues = null);
