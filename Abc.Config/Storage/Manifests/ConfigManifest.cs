using Abc.Config.Storage.Vfs;
using AbcDumper.Proto;
using ProtoBuf;

namespace Abc.Config.Storage.Manifests;

internal sealed class ConfigManifest(ConfigDownloadManifest download, ConfigLoadManifest index, (string Name, long Length)[] shards)
{
	private const string DownloadName = "ConfigDownloadManifest.bin";
	private const string LoadName = "ConfigLoadManifest.bin";
	private readonly HashSet<ulong> _field6Keys = [.. index.Field6Keys];

	public string AppVersion => index.AppVersion;
	public string ChangeList => download.ChangeList;
	public int RecordCount => index.RecordIndices.Count;
	public (string Name, long Length)[] Shards => shards;
	public IEnumerable<ConfigRecord> Records => index.RecordIndices.Select(pair => GetRecord(pair.Key, pair.Value));

	public static ConfigManifest Open(ManifestCipher cipher, string directory)
	{
		string loadPath = Path.Combine(directory, LoadName);
		var download = Serializer.Deserialize<ConfigDownloadManifest>(cipher.Decrypt(Path.Combine(directory, DownloadName)).AsMemory());
		var index = Serializer.Deserialize<ConfigLoadManifest>(cipher.Decrypt(loadPath).AsMemory());
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

		var shards = new (string Name, long Length)[index.ShardNames.Count];
		for (int i = 0; i < shards.Length; i++)
		{
			string name = index.ShardNames[i];
			if (!name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || name.AsSpan().IndexOfAny('/', '\\') >= 0
				|| !fileSizes.TryGetValue(name, out long length))
				throw new InvalidDataException($"Invalid or unlisted Config shard: {name}.");
			shards[i] = (name, length);
		}
		ValidateRows(index, shards);
		return new(download, index, shards);
	}

	public ConfigRecord Find(uint typeId, uint recordId)
	{
		ulong key = ((ulong)typeId << 32) | recordId;
		if (!index.RecordIndices.TryGetValue(key, out int row))
			throw new KeyNotFoundException($"Config record 0x{typeId:X8}:{recordId} was not found.");
		return GetRecord(key, row);
	}

	private ConfigRecord GetRecord(ulong key, int row) => new(
		(uint)(key >> 32), (uint)key, index.ShardIndices[row], index.Offsets[row], index.Lengths[row], _field6Keys.Contains(key));

	private static void ValidateRows(ConfigLoadManifest index, (string Name, long Length)[] shards)
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
