using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace AnimeSDK.Common.IO;

public sealed class ExportDirectory
{
	private readonly ConcurrentDictionary<string, byte> _owned = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

	public string DirectoryPath { get; }

	public ExportDirectory(string directory, params string[] readOnlyDirectories)
	{
		DirectoryPath = Path.GetFullPath(directory);
		foreach (string source in readOnlyDirectories)
		{
			RequireOutside(source);
		}
		SafePath.RequireNoReparsePoints(DirectoryPath);
		if (Directory.Exists(DirectoryPath) && Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
		{
			throw new IOException("导出目录必须为空：" + DirectoryPath);
		}
		Directory.CreateDirectory(DirectoryPath);
	}

	public void RequireOutside(string source)
	{
		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
		if (DirectoryPath.Equals(root, StringComparison.OrdinalIgnoreCase) || DirectoryPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new IOException("导出目录不能写入输入目录：" + root);
		}
	}

	public string Resolve(string relative)
	{
		string path = SafePath.CombineUnder(DirectoryPath, relative);
		SafePath.RequireNoReparsePoints(path);
		return path;
	}

	public void Write(string relative, Action<Stream> write)
	{
		string path = Resolve(relative);
		new AtomicFile(path).Write(write);
		_owned.TryAdd(path, 0);
	}

	public void Update(string relative, Action<Stream> write)
	{
		string path = Resolve(relative);
		if (!_owned.ContainsKey(path))
		{
			throw new IOException("只能更新当前导出会话创建的文件：" + path);
		}
		new AtomicFile(path).Write(write, overwrite: true);
	}
}
