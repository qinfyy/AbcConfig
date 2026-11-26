using Abc.Config.Storage.Manifests;
using AnimeSDK.Common.IO;

namespace Abc.Config.Storage.Vfs;

/// <summary>Provides bounded, read-only access to a Config package's indexed records.</summary>
public sealed class ConfigPackage
{
	private readonly ConfigManifest _manifest;
	private readonly ConfigShard[] _shards;

	public string AppVersion => _manifest.AppVersion;
	public string ChangeList => _manifest.ChangeList;
	public int RecordCount => _manifest.RecordCount;
	public int ShardCount => _shards.Length;
	public IEnumerable<ConfigRecord> Records => _manifest.Records;

	private ConfigPackage(ConfigManifest manifest, string directory, string? fallback)
	{
		_manifest = manifest;
		_shards = manifest.Shards.Select(shard => new ConfigShard(shard.Name, shard.Length,
			SafePath.CombineUnder(directory, shard.Name), fallback is null ? null : SafePath.CombineUnder(fallback, shard.Name))).ToArray();
	}

	/// <summary>Opens an installed update with an optional base package for missing shards.</summary>
	public static ConfigPackage Open(string directory, string? fallback, ReadOnlySpan<byte> manifestSeed)
	{
		using ManifestCipher cipher = new(manifestSeed);
		return new ConfigPackage(ConfigManifest.Open(cipher, directory), directory, fallback);
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
	public ConfigRecord Find(uint typeId, uint recordId) => _manifest.Find(typeId, recordId);

	/// <summary>Returns the manifest file name for the given shard index.</summary>
	public string GetShardName(int index) => _shards[index].Name;

	/// <summary>Returns a resource whose opened stream is limited to the indexed byte range.</summary>
	public BinaryResource GetResource(ConfigRecord record)
	{
		return _shards[record.ShardIndex].GetResource(record.Offset, record.Length);
	}
}

/// <summary>A Config record. Offset and Length are bytes relative to the indexed shard.</summary>
public readonly record struct ConfigRecord(uint TypeId, uint RecordId, int ShardIndex, int Offset, int Length, bool ListedInField6);

public sealed record ConfigShardReport(int Primary, int Fallback, string[] Problems);
