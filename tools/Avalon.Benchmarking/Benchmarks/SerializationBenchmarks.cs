using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Handshake;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[RPlotExporter]
public class SerializationBenchmarks
{
    private MemoryStream _unencryptedPacket;
    private MemoryStream _encryptedPacketAes128;
    private IAvalonCryptoSession _cryptoSession128;
    
    [GlobalSetup]
    public void Setup()
    {
        _unencryptedPacket = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(_unencryptedPacket, CClientInfoPacket.Create(new byte[]{0x04, 0x10}), PrefixStyle.Base128);
        _unencryptedPacket.Seek(0, SeekOrigin.Begin);
        
        var serverKeyPair128 = AsymmetricCipher.GenerateECDHKeyPair(128);
        var serverPublicKey128 = AsymmetricCipher.GetPublicKeyFromKeyPair(serverKeyPair128);
        var serverPublicKeyBytes128 = AsymmetricCipher.GetPublicKeyBytes(serverPublicKey128);
        
        _cryptoSession128 = new AvalonCryptoSession();
        _cryptoSession128.Initialize(serverPublicKeyBytes128);
        
        _encryptedPacketAes128 = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(_encryptedPacketAes128, CCharacterListPacket.Create(_cryptoSession128.Encrypt), PrefixStyle.Base128);
        _encryptedPacketAes128.Seek(0, SeekOrigin.Begin);
    }
    
    [Benchmark]
    public void Serialize_NoEncryption()
    {
        var packet = CClientInfoPacket.Create(new byte[]{0x04, 0x10});
        
        using var memoryStream = new MemoryStream();
    
        Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
    }
    
    [Benchmark]
    public void Serialize_Aes128()
    {

        var packet = CCharacterListPacket.Create(_cryptoSession128.Encrypt);
        
        using var memoryStream = new MemoryStream();
    
        Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
    }
    
    [Benchmark]
    public void Deserialize_Aes128()
    {
        _encryptedPacketAes128.Seek(0, SeekOrigin.Begin);
        
        var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_encryptedPacketAes128, PrefixStyle.Base128);
        
        byte[] decryptedBytes = new byte[packet.Payload.Length];
        int len = _cryptoSession128.Decrypt(packet.Payload.AsSpan(), decryptedBytes);

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
