using System;
using System.IO;

namespace AnimeSDK.Common.IO;

public sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) : Stream
{
	private int _position;

	private bool _disposed;

	public override bool CanRead => !_disposed;

	public override bool CanSeek => !_disposed;

	public override bool CanWrite => false;

	public override long Length
	{
		get
		{
			Check();
			return memory.Length;
		}
	}

	public override long Position
	{
		get
		{
			Check();
			return _position;
		}
		set
		{
			Seek(value, SeekOrigin.Begin);
		}
	}

	private void Check()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return Read(buffer.AsSpan(offset, count));
	}

	public override int Read(Span<byte> buffer)
	{
		Check();
		int count = Math.Min(buffer.Length, memory.Length - _position);
		memory.Span.Slice(_position, count).CopyTo(buffer);
		_position += count;
		return count;
	}

	public override int ReadByte()
	{
		Check();
		return (_position == memory.Length) ? (-1) : memory.Span[_position++];
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		Check();
		if (1 == 0)
		{
		}
		long num = checked(origin switch
		{
			SeekOrigin.Begin => offset,
			SeekOrigin.Current => _position + offset,
			SeekOrigin.End => memory.Length + offset,
			_ => throw new ArgumentOutOfRangeException("origin"),
		});
		if (1 == 0)
		{
		}
		long next = num;
		if (next < 0 || next > memory.Length)
		{
			throw new IOException("Seek exceeds the memory resource.");
		}
		return _position = (int)next;
	}

	protected override void Dispose(bool disposing)
	{
		_disposed = true;
		base.Dispose(disposing);
	}

	public override void Flush()
	{
		Check();
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}
}
