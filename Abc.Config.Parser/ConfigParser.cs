using Abc.Config.Core.Serialization;
using Abc.Config.Storage.Vfs;
using AbcDumper.Contracts.Model;

namespace Abc.Config.Parser;

/// <summary>Binds registered type IDs to Config contracts and reads indexed records.</summary>
public sealed class ConfigParser
{
	private readonly ConfigPackage _package;
	private readonly IReadOnlyDictionary<uint, string> _types;
	private readonly IReadOnlyDictionary<uint, string> _rawCodecs;
	private readonly ConfigRecordReader _reader = new();

	public ConfigParser(ConfigContract contract, ConfigPackage package)
	{
		contract.ValidateClient(package.AppVersion, package.ChangeList);
		_package = package;
		_types = contract.Types;
		_rawCodecs = contract.RawCodecs;
	}

	public ConfigRecordType Resolve(uint typeId)
	{
		string? schemaName = _types.GetValueOrDefault(typeId);
		string? contractName = _reader.GetContractName(schemaName);
		string? name = schemaName is not null && contractName is not null && contractName != schemaName[(schemaName.LastIndexOf('.') + 1)..]
			? contractName : schemaName;
		return new(schemaName, name, _rawCodecs.ContainsKey(typeId) ? "raw" : contractName is null ? "unknown" : "protobuf");
	}

	public ConfigDecodeResult Parse(ConfigRecord record)
	{
		if (_rawCodecs.TryGetValue(record.TypeId, out string? layout))
			return new(ConfigDecodeStatus.NonProtobuf, Issues: [new("", "nativeLayout", layout)]);
		string? schemaName = _types.GetValueOrDefault(record.TypeId);
		if (schemaName is null)
			return new(ConfigDecodeStatus.UnmappedType);
		if (_reader.GetContractName(schemaName) is null)
			return new(ConfigDecodeStatus.MissingSchema);
		try
		{
			byte[] bytes = new byte[record.Length];
			using Stream input = _package.GetResource(record).OpenRead();
			input.ReadExactly(bytes);
			return _reader.Read(bytes, schemaName);
		}
		catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			return new(ConfigDecodeStatus.ReadFailed, Issues: [new("", "recordReadError", error.Message)]);
		}
	}
}

public sealed record ConfigRecordType(string? SchemaName, string? Name, string Encoding);
