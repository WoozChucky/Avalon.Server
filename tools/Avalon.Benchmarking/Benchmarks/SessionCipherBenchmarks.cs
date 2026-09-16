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

    private IAvalonCryptoSession _client = null!;
    private IAvalonCryptoSession _server = null!;
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
        var clientPublicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(
            AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeyPair));

        // Both ends of one exchange. A session seals with its own direction's key and opens with
        // the other's, so a single session cannot read what it wrote.
        _client = new AvalonCryptoSession(CryptoRole.Client, clientKeyPair);
        _client.Initialize(serverPublicKeyBytes);

        _server = new AvalonCryptoSession(CryptoRole.Server, serverKeyPair);
        _server.Initialize(clientPublicKeyBytes);

        // The platform arm is keyed with the client-to-server key the session actually seals with,
        // so both arms run on identical key material rather than merely equivalent material.
        byte[] sharedSecret = AsymmetricCipher.CalculateSharedSecret(
            clientKeyPair,
            AsymmetricCipher.GetPublicKeyFromBytes(serverPublicKeyBytes));

        byte[] sessionKey = SessionKeys
            .Derive(sharedSecret, clientPublicKeyBytes, serverPublicKeyBytes)
            .ClientToServer;

        _aesGcm = new AesGcm(sessionKey, TagSize);

        _plaintext = new byte[PayloadSize];
        RandomNumberGenerator.Fill(_plaintext);

        _encrypted = _client.Encrypt(_plaintext);
        _output = new byte[PayloadSize + TagSize];
    }

    [GlobalCleanup]
    public void Cleanup() => _aesGcm.Dispose();

    [Benchmark]
    public byte[] BouncyCastle_Encrypt() => _client.Encrypt(_plaintext);

    [Benchmark]
    public int BouncyCastle_Decrypt() => _server.Decrypt(_encrypted, _output);

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
