using System;
using System.IO;

namespace AnimeSDK.Common.IO;

public sealed class AtomicFile
{
	public string Path { get; }

	public AtomicFile(string path)
	{
		Path = System.IO.Path.GetFullPath(path);
		SafePath.RequireNoReparsePoints(Path);
	}

	public void Write(Action<Stream> write, bool overwrite = false)
	{
		ArgumentNullException.ThrowIfNull(write, "write");
		SafePath.RequireNoReparsePoints(Path);
		if ((!overwrite && File.Exists(Path)) || Directory.Exists(Path))
		{
			throw new IOException("Output already exists: " + Path);
		}
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
		string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		bool created = false;
		try
		{
			using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				created = true;
				write(stream);
				stream.Flush();
			}
			SafePath.RequireNoReparsePoints(Path);
			File.Move(temporary, Path, overwrite);
			created = false;
		}
		finally
		{
			if (created)
			{
				File.Delete(temporary);
			}
		}
	}
}
