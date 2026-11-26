using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AnimeSDK.Common.IO;

public sealed class RandomAccessFile : IDisposable
{
	private sealed class SliceStream(SafeFileHandle handle, long offset, long length) : Stream
	{
		private long _position;

		private bool _disposed;

		public override bool CanRead => !_disposed && !handle.IsClosed;

		public override bool CanSeek => CanRead;

		public override bool CanWrite => false;

		public override long Length
		{
			get
			{
				Check();
				return length;
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
			ObjectDisposedException.ThrowIf(_disposed || handle.IsClosed, this);
		}

		public override int Read(byte[] buffer, int start, int count)
		{
			return Read(buffer.AsSpan(start, count));
		}

		public override int Read(Span<byte> buffer)
		{
			Check();
			int count = (int)Math.Min(buffer.Length, length - _position);
			int read = ((count != 0) ? RandomAccess.Read(handle, buffer.Slice(0, count), offset + _position) : 0);
			_position += read;
			return read;
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
		{
			Check();
			cancellationToken.ThrowIfCancellationRequested();
			int count = (int)Math.Min(buffer.Length, length - _position);
			int num = ((count != 0) ? (await RandomAccess.ReadAsync(handle, buffer.Slice(0, count), offset + _position, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)) : 0);
			int read = num;
			_position += read;
			return read;
		}

		public override long Seek(long value, SeekOrigin origin)
		{
			Check();
			if (1 == 0)
			{
			}
			long num = checked(origin switch
			{
				SeekOrigin.Begin => value,
				SeekOrigin.Current => _position + value,
				SeekOrigin.End => length + value,
				_ => throw new ArgumentOutOfRangeException("origin"),
			});
			if (1 == 0)
			{
			}
			long next = num;
			if (next < 0 || next > length)
			{
				throw new IOException("Seek exceeds the resource slice.");
			}
			return _position = next;
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

	private readonly SafeFileHandle _handle;

	public long Length { get; }

	public RandomAccessFile(string path)
	{
		_handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess, 0L);
		try
		{
			Length = RandomAccess.GetLength(_handle);
		}
		catch
		{
			_handle.Dispose();
			throw;
		}
	}

	public Stream OpenRead(long offset = 0L, long? length = null)
	{
		ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
		long size = length ?? (Length - offset);
		if (offset < 0 || size < 0 || offset > Length || size > Length - offset)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		return new SliceStream(_handle, offset, size);
	}

	public void Dispose()
	{
		_handle.Dispose();
	}
}
