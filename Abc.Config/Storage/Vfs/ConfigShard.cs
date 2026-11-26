using AnimeSDK.Common.IO;

namespace Abc.Config.Storage.Vfs;

internal sealed class ConfigShard(string name, long length, string primary, string? fallback)
{
	public string Primary => primary;
	public string Name => name;

	public string Resolve()
	{
		string? path = File.Exists(primary) ? primary : fallback;
		if (path is null || !File.Exists(path))
			throw new FileNotFoundException($"Config shard not found: {name}.", name);
		long actualLength = new FileInfo(path).Length;
		if (actualLength != length)
			throw new InvalidDataException($"Config shard {name} has length {actualLength}, expected {length}.");
		return path;
	}

	public BinaryResource GetResource(int offset, int size)
	{
		if (offset < 0 || size < 0 || (long)offset + size > length)
			throw new InvalidDataException($"Config range {offset}+{size} is outside shard {name}.");
		return new(size, () =>
		{
			FileStream stream = File.OpenRead(Resolve());
			try { return new BoundedReadStream(stream, offset, size, leaveOpen: false); }
			catch { stream.Dispose(); throw; }
		});
	}
}
