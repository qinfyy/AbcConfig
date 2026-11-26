using System;
using System.IO;

namespace AnimeSDK.Common.IO;

public sealed class BinaryResource
{
	private readonly Func<Stream> _open;

	public long Length { get; }

	public BinaryResource(long length, Func<Stream> open)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(length, "length");
		ArgumentNullException.ThrowIfNull(open, "open");
		Length = length;
		_open = open;
	}

	public Stream OpenRead()
	{
		return _open();
	}

	public Stream OpenRead(long offset, long length)
	{
		if (offset < 0 || length < 0 || offset > Length || length > Length - offset)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		Stream stream = OpenRead();
		try
		{
			return new BoundedReadStream(stream, offset, length, leaveOpen: false);
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}
}
