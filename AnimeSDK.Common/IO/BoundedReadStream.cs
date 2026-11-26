using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeSDK.Common.IO;

public sealed class BoundedReadStream : Stream
{
	private readonly Stream _stream;

	private readonly long _offset;

	private readonly long _length;

	private readonly bool _leaveOpen;

	private long _position;

	private bool _disposed;

	public override bool CanRead => !_disposed && _stream.CanRead;

	public override bool CanSeek => CanRead && _stream.CanSeek;

	public override bool CanWrite => false;

	public override long Length
	{
		get
		{
			Check();
			return _length;
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

	public BoundedReadStream(Stream stream, long offset, long length, bool leaveOpen = true)
	{
		ArgumentNullException.ThrowIfNull(stream, "stream");
		if (!stream.CanRead || !stream.CanSeek)
		{
			throw new ArgumentException("A readable seekable stream is required.", "stream");
		}
		if (offset < 0 || length < 0 || offset > stream.Length || length > stream.Length - offset)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		_stream = stream;
		_offset = offset;
		_length = length;
		_leaveOpen = leaveOpen;
		_stream.Position = offset;
	}

	private void Check()
	{
		ObjectDisposedException.ThrowIf(_disposed || !_stream.CanRead, this);
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return Read(buffer.AsSpan(offset, count));
	}

	public override int Read(Span<byte> buffer)
	{
		Check();
		int count = (int)Math.Min(buffer.Length, _length - _position);
		int read = ((count != 0) ? _stream.Read(buffer.Slice(0, count)) : 0);
		_position += read;
		return read;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
	{
		Check();
		cancellationToken.ThrowIfCancellationRequested();
		int count = (int)Math.Min(buffer.Length, _length - _position);
		int num = ((count != 0) ? (await _stream.ReadAsync(buffer.Slice(0, count), cancellationToken).ConfigureAwait(continueOnCapturedContext: false)) : 0);
		int read = num;
		_position += read;
		return read;
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		Check();
		if (1 == 0)
		{
		}
		checked
		{
			long num = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _position + offset,
				SeekOrigin.End => _length + offset,
				_ => throw new ArgumentOutOfRangeException("origin"),
			};
			if (1 == 0)
			{
			}
			long next = num;
			if (next < 0 || next > _length)
			{
				throw new IOException("Seek exceeds the bounded resource.");
			}
			_stream.Position = _offset + next;
			return _position = next;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if ((!_disposed & disposing) && !_leaveOpen)
		{
			_stream.Dispose();
		}
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
