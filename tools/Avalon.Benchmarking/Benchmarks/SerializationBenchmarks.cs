using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net70)]
[SimpleJob(RuntimeMoniker.Net80)]
[RPlotExporter]
public class SerializationBenchmarks
{
    private MemoryStream _unencryptedPacket;
    private MemoryStream _encryptedPacket_Aes128;
    private MemoryStream _encryptedPacket_Aes128_Std;
    private MemoryStream _encryptedPacket_Aes256_Std;

    private IAvalonCryptoSession _cryptoSessionStd256;
    private IAvalonCryptoSession _cryptoSessionStd128;
    private IAvalonCryptoSession _cryptoSession128;
    
    [GlobalSetup]
    public void Setup()
    {
        _unencryptedPacket = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(_unencryptedPacket, CCharacterLoadedPacket.Create(1), PrefixStyle.Base128);
        _unencryptedPacket.Seek(0, SeekOrigin.Begin);
        
        var serverKeyPair128 = AsymmetricCipher.GenerateECDHKeyPair(128);
        var serverPublicKey128 = AsymmetricCipher.GetPublicKeyFromKeyPair(serverKeyPair128);
        var serverPublicKeyBytes128 = AsymmetricCipher.GetPublicKeyBytes(serverPublicKey128);
        
        _cryptoSession128 = new AvalonCryptoSession();
        _cryptoSession128.Initialize(serverPublicKeyBytes128);
        
        _encryptedPacket_Aes128 = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(_encryptedPacket_Aes128, CCharacterListPacket.Create(1, _cryptoSession128.Encrypt), PrefixStyle.Base128);
        _encryptedPacket_Aes128.Seek(0, SeekOrigin.Begin);
    }
    
    [Benchmark]
    public void Serialize_NoEncryption()
    {
        var packet = CCharacterLoadedPacket.Create(1);
        
        using var memoryStream = new MemoryStream();
    
        Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
    }
    
    [Benchmark]
    public void Serialize_Aes128()
    {

        var packet = CCharacterListPacket.Create(1, _cryptoSession128.Encrypt);
        
        using var memoryStream = new MemoryStream();
    
        Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
    }
    
    [Benchmark]
    public void Deserialize_Aes128()
    {
        _encryptedPacket_Aes128.Seek(0, SeekOrigin.Begin);
        
        var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_encryptedPacket_Aes128, PrefixStyle.Base128);
        
        var decryptedBytes = _cryptoSession128.Decrypt(packet.Payload);
        
        using var memoryStream = new MemoryStream(decryptedBytes);
        
        var innerPacket = Serializer.Deserialize<CCharacterLoadedPacket>(memoryStream);
        if (innerPacket is not { AccountId: 1 })
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
        if (innerPacket is not { AccountId: 1 })
        {
            throw new Exception("Failed to deserialize packet");
        }
    }
    
}
