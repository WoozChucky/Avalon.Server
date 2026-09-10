using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Handshake;
using BenchmarkDotNet.Attributes;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures Protobuf-net packet serialization and deserialization, with and without the
/// AES-GCM session layer. Both sides of the key agreement use P-256, the curve
/// <c>CryptoManager</c> and <c>AvalonCryptoSession</c> use in production, so the session
/// key and cipher configuration match what a live connection negotiates.
/// </summary>
[MemoryDiagnoser]
[RPlotExporter]
public class SerializationBenchmarks
{
    private MemoryStream _unencryptedPacket = null!;
    private MemoryStream _encryptedPacket = null!;
    private IAvalonCryptoSession _cryptoSession = null!;

    [GlobalSetup]
    public void Setup()
    {
        _unencryptedPacket = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(_unencryptedPacket, CClientInfoPacket.Create(new byte[]{0x04, 0x10}), PrefixStyle.Base128);
        _unencryptedPacket.Seek(0, SeekOrigin.Begin);

        var serverKeyPair = AsymmetricCipher.GenerateECDHKeyPair(256);
        var serverPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(serverKeyPair);
        var serverPublicKeyBytes = AsymmetricCipher.GetPublicKeyBytes(serverPublicKey);

        _cryptoSession = new AvalonCryptoSession();
        _cryptoSession.Initialize(serverPublicKeyBytes);

        _encryptedPacket = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(_encryptedPacket, CCharacterListPacket.Create(_cryptoSession.Encrypt), PrefixStyle.Base128);
        _encryptedPacket.Seek(0, SeekOrigin.Begin);
    }

    [Benchmark]
    public void Serialize_NoEncryption()
    {
        var packet = CClientInfoPacket.Create(new byte[]{0x04, 0x10});

        using var memoryStream = new MemoryStream();

        Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
    }

    [Benchmark]
    public void Serialize_Encrypted()
    {
        var packet = CCharacterListPacket.Create(_cryptoSession.Encrypt);

        using var memoryStream = new MemoryStream();

        Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
    }

    [Benchmark]
    public void Deserialize_Encrypted()
    {
        _encryptedPacket.Seek(0, SeekOrigin.Begin);

        var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_encryptedPacket, PrefixStyle.Base128);

        byte[] decryptedBytes = new byte[packet.Payload.Length];
        int len = _cryptoSession.Decrypt(packet.Payload.AsSpan(), decryptedBytes);

        using var memoryStream = new MemoryStream(decryptedBytes, 0, len);

        var innerPacket = Serializer.Deserialize<CCharacterLoadedPacket>(memoryStream);
        if (innerPacket is null)
        {
            throw new Exception("Failed to deserialize packet");
        }
    }

    [Benchmark]
    public void Deserialize_NoEncryption()
    {
        _unencryptedPacket.Seek(0, SeekOrigin.Begin);
        var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_unencryptedPacket, PrefixStyle.Base128);

        using var memoryStream = new MemoryStream(packet.Payload);
        var innerPacket = Serializer.Deserialize<CCharacterListPacket>(memoryStream);
        if (innerPacket is null)
        {
            throw new Exception("Failed to deserialize packet");
        }
    }
}
