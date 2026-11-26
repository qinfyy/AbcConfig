using System;
using System.Buffers;
using System.IO.Hashing;
using System.Text;

namespace AnimeSDK.Common.Hashing;

public static class ResourceHash
{
	public static ulong XxHash64Utf8(string value)
	{
		ArgumentNullException.ThrowIfNull(value, "value");
		int size = Encoding.UTF8.GetByteCount(value);
		byte[] rented = null;
		Span<byte> span = ((size > 1024) ? (rented = ArrayPool<byte>.Shared.Rent(size)).AsSpan(0, size) : stackalloc byte[size]);
		Span<byte> bytes = span;
		try
		{
			Encoding.UTF8.GetBytes(value.AsSpan(), bytes);
			return XxHash64.HashToUInt64(bytes, 0L);
		}
		finally
		{
			if (rented != null)
			{
				ArrayPool<byte>.Shared.Return(rented);
			}
		}
	}

	public static int LegacyDjb2Utf8(string value)
	{
		ArgumentNullException.ThrowIfNull(value, "value");
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		int first = 5381;
		int second = 5381;
		for (int index = 0; index < bytes.Length; index += 2)
		{
			first = (first * 33) ^ bytes[index];
			if (index + 1 < bytes.Length)
			{
				second = (second * 33) ^ bytes[index + 1];
			}
		}
		return first + second * 1566083941;
	}
}
