using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Compares the session cipher as the packet pipeline calls it — BouncyCastle AES-GCM behind
/// <c>AvalonCryptoSession.Encrypt</c> / <c>Decrypt</c> — against the platform's
/// <c>System.Security.Cryptography.AesGcm</c> over the same key and the same
/// nonce + ciphertext + tag layout.
///
/// <para>
/// The key comes from a real P-256 ECDH agreement, so both arms run on identical 256-bit key
/// material. The BouncyCastle arm keeps the production call shape: a lock, a per-call cipher
/// <c>Init</c>, and a freshly allocated result. The platform arm allocates an equivalent result
/// buffer, so the gap between them is per-call cipher overhead rather than buffer strategy.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SessionCipherBenchmarks
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private IAvalonCryptoSession _session = null!;
    private AesGcm _aesGcm = null!;

    private byte[] _plaintext = null!;
    private byte[] _encrypted = null!;
    private byte[] _output = null!;

    [Params(64, 256, 1024)]
    public int PayloadSize;

    [GlobalSetup]
    public void Setup()
    {
        var serverKeyPair = AsymmetricCipher.GenerateECDHKeyPair(256);
        var serverPublicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(
            AsymmetricCipher.GetPublicKeyFromKeyPair(serverKeyPair));

        var clientKeyPair = AsymmetricCipher.GenerateECDHKeyPair(256);

        _session = new AvalonCryptoSession(clientKeyPair);
        _session.Initialize(serverPublicKeyBytes);

        // The session derives its AES key from this same agreement, so the platform arm is keyed
        // identically rather than merely equivalently.
        byte[] sessionKey = AsymmetricCipher.CalculateSharedSecret(
            clientKeyPair,
            AsymmetricCipher.GetPublicKeyFromBytes(serverPublicKeyBytes));

        _aesGcm = new AesGcm(sessionKey, TagSize);

        _plaintext = new byte[PayloadSize];
        RandomNumberGenerator.Fill(_plaintext);

        _encrypted = _session.Encrypt(_plaintext);
        _output = new byte[PayloadSize + TagSize];
    }

    [GlobalCleanup]
    public void Cleanup() => _aesGcm.Dispose();

    [Benchmark]
    public byte[] BouncyCastle_Encrypt() => _session.Encrypt(_plaintext);

    [Benchmark]
    public int BouncyCastle_Decrypt() => _session.Decrypt(_encrypted, _output);

    [Benchmark]
    public byte[] AesGcm_Encrypt()
    {
        var result = new byte[NonceSize + PayloadSize + TagSize];

        RandomNumberGenerator.Fill(result.AsSpan(0, NonceSize));

        _aesGcm.Encrypt(
            result.AsSpan(0, NonceSize),
            _plaintext,
            result.AsSpan(NonceSize, PayloadSize),
            result.AsSpan(NonceSize + PayloadSize, TagSize));

        return result;
    }

    [Benchmark]
    public int AesGcm_Decrypt()
    {
        _aesGcm.Decrypt(
            _encrypted.AsSpan(0, NonceSize),
            _encrypted.AsSpan(NonceSize, PayloadSize),
            _encrypted.AsSpan(NonceSize + PayloadSize, TagSize),
            _output.AsSpan(0, PayloadSize));

        return PayloadSize;
    }
}
