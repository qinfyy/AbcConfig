using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Abc.Config.Storage.Manifests;

internal sealed class ManifestCipher : IDisposable
{
	private const int IvSize = 16;
	private const int MaxManifestSize = 64 * 1024 * 1024;
	private readonly Aes _aes;

	public ManifestCipher(ReadOnlySpan<byte> seed)
	{
		if (seed.Length != 32)
			throw new ArgumentException("Expected a 32-byte Config manifest seed.", nameof(seed));
		Span<byte> input = stackalloc byte[sizeof(ulong)];
		Span<byte> key = stackalloc byte[32];
		// Both Config manifest readers pass zero to the native decrypt wrapper.
		BinaryPrimitives.WriteUInt64LittleEndian(input, 0);
		HMACSHA256.HashData(seed, input, key);
		_aes = Aes.Create();
		try
		{
			_aes.SetKey(key);
		}
		catch
		{
			_aes.Dispose();
			throw;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	public byte[] Decrypt(string path)
	{
		using FileStream source = File.OpenRead(path);
		long size = source.Length;
		if (size < IvSize * 2 || size > MaxManifestSize || size % IvSize != 0)
			throw new InvalidDataException($"Invalid encrypted manifest size: {Path.GetFileName(path)} ({size} bytes).");
		Span<byte> iv = stackalloc byte[IvSize];
		source.ReadExactly(iv);
		byte[] ciphertext = new byte[size - IvSize];
		source.ReadExactly(ciphertext);
		return _aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);
	}

	public void Dispose() => _aes.Dispose();
}
