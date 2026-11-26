using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace AbcDumper.Bin.Storage;

internal sealed class ManifestCipher : IDisposable
{
	private const int IvSize = 16;
	private const int MaxManifestSize = 64 * 1024 * 1024;
	private readonly byte[] _key;

	public ManifestCipher(ReadOnlySpan<byte> seed)
	{
		if (seed.Length != 32)
			throw new ArgumentException("Expected a 32-byte Config manifest seed.", nameof(seed));
		HMac hmac = new(new Sha256Digest());
		hmac.Init(new KeyParameter(seed));
		// Both Config manifest readers pass zero to the native decrypt wrapper.
		hmac.BlockUpdate(new byte[sizeof(ulong)]);
		_key = new byte[hmac.GetMacSize()];
		hmac.DoFinal(_key);
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

		PaddedBufferedBlockCipher cipher = new(new CbcBlockCipher(new AesEngine()), new Pkcs7Padding());
		cipher.Init(false, new ParametersWithIV(new KeyParameter(_key), iv));
		try
		{
			return cipher.DoFinal(ciphertext);
		}
		catch (InvalidCipherTextException error)
		{
			throw new CryptographicException($"Invalid encrypted manifest padding: {Path.GetFileName(path)}.", error);
		}
	}

	public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
