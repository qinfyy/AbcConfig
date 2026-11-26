using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace AnimeSDK.Common.IO;

public sealed class EndianReader(Stream stream)
{
	private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	public long Remaining => stream.Length - stream.Position;

	public byte ReadByte()
	{
		int value = stream.ReadByte();
		if (value >= 0)
		{
			return (byte)value;
		}
		throw new EndOfStreamException();
	}

	public ushort ReadUInt16BigEndian()
	{
		Span<byte> data = stackalloc byte[2];
		stream.ReadExactly(data);
		return BinaryPrimitives.ReadUInt16BigEndian(data);
	}

	public uint ReadUInt32BigEndian()
	{
		Span<byte> data = stackalloc byte[4];
		stream.ReadExactly(data);
		return BinaryPrimitives.ReadUInt32BigEndian(data);
	}

	public ulong ReadUInt64BigEndian()
	{
		Span<byte> data = stackalloc byte[8];
		stream.ReadExactly(data);
		return BinaryPrimitives.ReadUInt64BigEndian(data);
	}

	public ushort ReadUInt16LittleEndian()
	{
		Span<byte> data = stackalloc byte[2];
		stream.ReadExactly(data);
		return BinaryPrimitives.ReadUInt16LittleEndian(data);
	}

	public uint ReadUInt32LittleEndian()
	{
		Span<byte> data = stackalloc byte[4];
		stream.ReadExactly(data);
		return BinaryPrimitives.ReadUInt32LittleEndian(data);
	}

	public ulong ReadUInt64LittleEndian()
	{
		Span<byte> data = stackalloc byte[8];
		stream.ReadExactly(data);
		return BinaryPrimitives.ReadUInt64LittleEndian(data);
	}

	public byte[] ReadBytes(int count)
	{
		byte[] result = new byte[count];
		stream.ReadExactly(result);
		return result;
	}

	public string ReadUtf8(int count)
	{
		return Utf8.GetString(ReadBytes(count));
	}

	public int ReadCount(int minimumSize, int maximum)
	{
		uint value = ReadUInt32BigEndian();
		if (value > maximum || value > Remaining / minimumSize)
		{
			throw new InvalidDataException("Invalid manifest count: " + value);
		}
		return (int)value;
	}

	public void RequireEnd()
	{
		if (Remaining != 0)
		{
			throw new InvalidDataException("Unexpected trailing manifest bytes: " + Remaining);
		}
	}
}
