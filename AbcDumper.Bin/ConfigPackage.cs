using AbcDumper.Bin.Storage;
using AnimeSDK.Common.IO;

namespace AbcDumper.Bin;

/// <summary>Provides bounded, read-only access to a Config package's indexed records.</summary>
public sealed class ConfigPackage
{
	private readonly ConfigShard[] _shards;

	public ConfigIndex Index { get; }
	public string AppVersion => Index.AppVersion;
	public string ChangeList => Index.ChangeList;
	public int RecordCount => Index.RecordCount;
	public int ShardCount => _shards.Length;
	public IEnumerable<ConfigRecord> Records => Index.Records;

	private ConfigPackage(ConfigIndex index, string directory, string? fallback)
	{
		Index = index;
		_shards = index.Shards.Select(shard => new ConfigShard(shard, directory, fallback)).ToArray();
	}

	/// <summary>Opens an installed update with an optional base package for missing shards.</summary>
	public static ConfigPackage Open(string directory, ReadOnlySpan<byte> manifestSeed, string? fallback = null) =>
		new(ConfigIndex.Open(directory, manifestSeed), directory, fallback);

	public static ConfigPackage OpenGame(string gameDirectory, string changeList, ReadOnlySpan<byte> manifestSeed)
	{
		ArgumentException.ThrowIfNullOrEmpty(changeList);
		if (changeList.Any(value => value is < '0' or > '9'))
			throw new ArgumentException("Expected a numeric Config changelist.", nameof(changeList));
		string data = Path.Combine(gameDirectory, "NexusAnima_Data");
		ConfigPackage package = Open(Path.Combine(data, "PersistentAssets", "Bin", changeList), manifestSeed,
			Path.Combine(data, "StreamingAssets", "Bin"));
		if (package.ChangeList != changeList)
			throw new InvalidDataException($"Config package changelist {package.ChangeList} does not match {changeList}.");
		return package;
	}

	/// <summary>Checks that all indexed shard files exist at their declared sizes.</summary>
	public ConfigShardReport InspectShards()
	{
		int primary = 0, fallback = 0;
		List<string> problems = [];
		foreach (ConfigShard shard in _shards)
		{
			try
			{
				if (shard.Resolve() == shard.Primary)
					primary++;
				else
					fallback++;
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
			{
				problems.Add(ex.Message);
			}
		}
		return new(primary, fallback, problems.ToArray());
	}

	/// <summary>Looks up a record by the two halves of its unsigned 64-bit index key.</summary>
	public ConfigRecord Find(uint typeId, uint recordId) => Index.Find(typeId, recordId);

	public bool TryFind(uint typeId, uint recordId, out ConfigRecord record) => Index.TryFind(typeId, recordId, out record);

	/// <summary>Returns a resource whose opened stream is limited to the indexed byte range.</summary>
	public BinaryResource GetResource(ConfigRecord record)
	{
		if (!TryFind(record.TypeId, record.RecordId, out ConfigRecord indexed) || indexed != record)
			throw new ArgumentException("The record does not match this Config index.", nameof(record));
		return _shards[record.ShardIndex].GetResource(record.Offset, record.Length);
	}

	public byte[] ReadBytes(ConfigRecord record)
	{
		BinaryResource resource = GetResource(record);
		using Stream input = resource.OpenRead();
		byte[] bytes = new byte[resource.Length];
		input.ReadExactly(bytes);
		return bytes;
	}

	public byte[] ReadBytes(uint typeId, uint recordId) => ReadBytes(Find(typeId, recordId));
}

public sealed record ConfigShardReport(int Primary, int Fallback, string[] Problems);
