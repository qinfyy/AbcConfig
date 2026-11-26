using Abc.Bin.Serialization;
using AbcDumper.Bin.Storage;
using AbcDumper.Proto;

namespace AbcDumper.Bin;

/// <summary>Reads the Config manifests and indexes record locations without opening any shards.</summary>
public sealed class ConfigIndex
{
	private const string DownloadName = "ConfigDownloadManifest.bin";
	private const string LoadName = "ConfigLoadManifest.bin";
	private static readonly AbcProtoSerializer Serializer = new();
	private readonly ConfigLoadManifest _index;
	private readonly HashSet<ulong> _field6Keys;

	public string AppVersion => _index.AppVersion;
	public string ChangeList { get; }
	public int RecordCount => _index.RecordIndices.Count;
	public IReadOnlyList<ConfigShardInfo> Shards { get; }
	public IEnumerable<ConfigRecord> Records => _index.RecordIndices.Select(pair => GetRecord(pair.Key, pair.Value));

	private ConfigIndex(string changeList, ConfigLoadManifest index, ConfigShardInfo[] shards)
	{
		ChangeList = changeList;
		_index = index;
		_field6Keys = [.. index.Field6Keys];
		Shards = Array.AsReadOnly(shards);
	}

	public static ConfigIndex Open(string directory, ReadOnlySpan<byte> manifestSeed)
	{
		using ManifestCipher cipher = new(manifestSeed);
		string loadPath = Path.Combine(directory, LoadName);
		var download = Serializer.Deserialize<ConfigDownloadManifest>(cipher.Decrypt(Path.Combine(directory, DownloadName)));
		var index = Serializer.Deserialize<ConfigLoadManifest>(cipher.Decrypt(loadPath));
		if (download.AppVersion.Length == 0 || download.ChangeList.Length == 0 || download.Files.Count == 0)
			throw new InvalidDataException("ConfigDownloadManifest is missing version, changelist, or files.");
		if (download.AppVersion != index.AppVersion)
			throw new InvalidDataException("Config manifest versions disagree.");

		Dictionary<string, long> fileSizes = new(StringComparer.Ordinal);
		foreach (ConfigDownloadFile file in download.Files)
			if (file.Length <= 0 || !fileSizes.TryAdd(file.Name, file.Length))
				throw new InvalidDataException($"Invalid or duplicate download entry: {file.Name}.");
		if (!fileSizes.TryGetValue(LoadName, out long indexSize) || new FileInfo(loadPath).Length != indexSize)
			throw new InvalidDataException("ConfigLoadManifest.bin does not match the download manifest size.");

		ConfigShardInfo[] shards = new ConfigShardInfo[index.ShardNames.Count];
		for (int i = 0; i < shards.Length; i++)
		{
			string name = index.ShardNames[i];
			if (!name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || name.AsSpan().IndexOfAny('/', '\\') >= 0
				|| !fileSizes.TryGetValue(name, out long length))
				throw new InvalidDataException($"Invalid or unlisted Config shard: {name}.");
			shards[i] = new(name, length);
		}
		ValidateRows(index, shards);
		return new(download.ChangeList, index, shards);
	}

	public ConfigRecord Find(uint typeId, uint recordId) => TryFind(typeId, recordId, out ConfigRecord record)
		? record : throw new KeyNotFoundException($"Config record 0x{typeId:X8}:{recordId} was not found.");

	public bool TryFind(uint typeId, uint recordId, out ConfigRecord record)
	{
		ulong key = ((ulong)typeId << 32) | recordId;
		bool found = _index.RecordIndices.TryGetValue(key, out int row);
		record = found ? GetRecord(key, row) : default;
		return found;
	}

	private ConfigRecord GetRecord(ulong key, int row) => new(
		(uint)(key >> 32), (uint)key, _index.ShardIndices[row], _index.Offsets[row], _index.Lengths[row], _field6Keys.Contains(key));

	private static void ValidateRows(ConfigLoadManifest index, ConfigShardInfo[] shards)
	{
		int rows = index.ShardIndices.Count;
		if (shards.Length == 0 || rows == 0 || index.RecordIndices.Count == 0 || index.Offsets.Count != rows || index.Lengths.Count != rows)
			throw new InvalidDataException("ConfigLoadManifest is missing its record index or has inconsistent columns.");
		foreach (int row in index.RecordIndices.Values)
			if ((uint)row >= (uint)rows)
				throw new InvalidDataException($"ConfigLoadManifest contains an out-of-range row: {row}.");
		for (int row = 0; row < rows; row++)
		{
			int shard = index.ShardIndices[row], offset = index.Offsets[row], size = index.Lengths[row];
			if ((uint)shard >= (uint)shards.Length || offset < 0 || size < 0 || (long)offset + size > shards[shard].Length)
				throw new InvalidDataException($"Config record row {row} has an invalid shard range.");
		}
	}
}

public readonly record struct ConfigShardInfo(string Name, long Length);

/// <summary>A record location. Offset and Length are bytes relative to its shard.</summary>
public readonly record struct ConfigRecord(uint TypeId, uint RecordId, int ShardIndex, int Offset, int Length, bool ListedInField6);
