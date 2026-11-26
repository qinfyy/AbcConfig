using AnimeSDK.Common.IO;

namespace AbcDumper.Bin.Storage;

internal sealed class ConfigShard(ConfigShardInfo info, string directory, string? baseDirectory)
{
	private readonly ConfigShardInfo _info = info;
	private readonly string _primary = SafePath.CombineUnder(directory, info.Name);
	private readonly string? _fallback = baseDirectory is null ? null : SafePath.CombineUnder(baseDirectory, info.Name);
	public string Primary => _primary;

	public string Resolve()
	{
		string? path = File.Exists(_primary) ? _primary : _fallback;
		if (path is null || !File.Exists(path))
			throw new FileNotFoundException($"Config shard not found: {_info.Name}.", _info.Name);
		long actualLength = new FileInfo(path).Length;
		if (actualLength != _info.Length)
			throw new InvalidDataException($"Config shard {_info.Name} has length {actualLength}, expected {_info.Length}.");
		return path;
	}

	public BinaryResource GetResource(int offset, int size)
	{
		if (offset < 0 || size < 0 || (long)offset + size > _info.Length)
			throw new InvalidDataException($"Config range {offset}+{size} is outside shard {_info.Name}.");
		return new(size, () =>
		{
			FileStream stream = File.OpenRead(Resolve());
			try { return new BoundedReadStream(stream, offset, size, leaveOpen: false); }
			catch { stream.Dispose(); throw; }
		});
	}
}
