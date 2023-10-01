using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Tcp;
using Avalon.Network.Udp;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Avalon.Client.Simulator
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
            var serverKeyPair = AsymmetricCipher.GenerateECDHKeyPair();
            var clientKeyPair = AsymmetricCipher.GenerateECDHKeyPair();
            var serverPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(serverKeyPair);
            var clientPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeyPair);
                
            var _serverSharedKey = AsymmetricCipher.CalculateSharedSecret(serverKeyPair, clientPublicKey);
            var _clientSharedKey = AsymmetricCipher.CalculateSharedSecret(clientKeyPair, serverPublicKey);
            
            
            if (_serverSharedKey.Length != 16 || _clientSharedKey.Length != 16)
            {
                throw new Exception("Invalid shared key length");
            }
            
            var summary = BenchmarkRunner.Run<MemoryBenchmark>();
            //var proto = Serializer.GetProto<CAuthPacket>();
            /*
            const int clientCount = 4; // This is the number of clients to simulate

            const string username = "test";
            const string password = "test";

            for (var i = 0; i < clientCount; i++)
            {
                // how create a thread for each client and run them
                var client = new SimulatedClient($"{username}{i}", password);
                await Task.Run(() => client.Run());
                await Task.Delay(1000);
            }

            while (true)
            {

            }
            */


        }
        
        [MemoryDiagnoser]
        [SimpleJob(RuntimeMoniker.Net70)]
        [SimpleJob(RuntimeMoniker.Net80)]
        [RPlotExporter]
        public class MemoryBenchmark
        {
            private Aes _aes256Weak;
            private Aes _aes128;
            private SecureRandom _secureRandom = new();
            private MemoryStream _unencryptedPacket;
            private MemoryStream _encryptedPacket_Aes256_Weak;
            private MemoryStream _encryptedPacket_Aes128;
            private byte[] _serverSharedKey;
            private byte[] _clientSharedKey;
            
            [GlobalSetup]
            public void Setup()
            {
                var aes256InsecureKey = new byte[32]; // 256 bits = 32 bytes
                using (var rng = new RNGCryptoServiceProvider())
                {
                    rng.GetBytes(aes256InsecureKey);
                }
                _aes256Weak = Aes.Create();
                _aes256Weak.Key = aes256InsecureKey;
                _aes256Weak.IV = new byte[]
                    { 0x5A, 0x36, 0x7F, 0x8D, 0xE9, 0x02, 0xC4, 0xAF, 0x71, 0x5E, 0x9B, 0x44, 0xD7, 0x1A, 0x80, 0x3F };
                
                _aes128 = Aes.Create();
                _aes128.KeySize = 128;
                
                _unencryptedPacket = new MemoryStream();
                Serializer.SerializeWithLengthPrefix(_unencryptedPacket, CCharacterLoadedPacket.Create(1), PrefixStyle.Base128);
                _unencryptedPacket.Seek(0, SeekOrigin.Begin);
                
                _encryptedPacket_Aes256_Weak = new MemoryStream();
                Serializer.SerializeWithLengthPrefix(_encryptedPacket_Aes256_Weak, CCharacterListPacket.Create(1, Encrypt), PrefixStyle.Base128);
                _encryptedPacket_Aes256_Weak.Seek(0, SeekOrigin.Begin);
                
                var serverKeyPair = AsymmetricCipher.GenerateECDHKeyPair();
                var clientKeyPair = AsymmetricCipher.GenerateECDHKeyPair();
                var serverPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(serverKeyPair);
                var clientPublicKey = AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeyPair);
                
                _serverSharedKey = AsymmetricCipher.CalculateSharedSecret(serverKeyPair, clientPublicKey);
                _clientSharedKey = AsymmetricCipher.CalculateSharedSecret(clientKeyPair, serverPublicKey);
                
                if (_serverSharedKey.Length != 16 || _clientSharedKey.Length != 16)
                {
                    throw new Exception("Invalid shared key length");
                }
                
                _encryptedPacket_Aes128 = new MemoryStream();
                Serializer.SerializeWithLengthPrefix(_encryptedPacket_Aes128, CCharacterListPacket.Create(1, bytes => EncryptStrong(_serverSharedKey, bytes)), PrefixStyle.Base128);
                _encryptedPacket_Aes128.Seek(0, SeekOrigin.Begin);
            }

            private byte[] Encrypt(byte[] data)
            {
                using var memoryStream = new MemoryStream();
                using (var encryptor = _aes256Weak.CreateEncryptor())
                {
                    using (var csEncrypt = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                    {
                        csEncrypt.Write(data, 0, data.Length);
                        csEncrypt.FlushFinalBlock();
                    }
                }

                return memoryStream.ToArray();
            }
            
            private byte[] Decrypt(byte[] data)
            {
                using var memoryStream = new MemoryStream();
                using (var decryptor = _aes256Weak.CreateDecryptor())
                {
                    using (var csDecrypt = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Write))
                    {
                        csDecrypt.Write(data, 0, data.Length);
                        csDecrypt.FlushFinalBlock();
                    }
                }

                return memoryStream.ToArray();
            }
            
            private byte[] EncryptStrong(byte[] key, byte[] data)
            {
                // Generate a random 96-bit nonce (IV)
                var nonce = new byte[12];
                _secureRandom.NextBytes(nonce);

                // Create an AES-GCM cipher
                var cipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
                var parameters = new ParametersWithIV(new KeyParameter(key), nonce);
                cipher.Init(true, parameters);

                // Encrypt the data
                var ciphertext = cipher.DoFinal(data);

                // Combine the nonce and ciphertext
                var encryptedData = nonce.Concat(ciphertext).ToArray();
        
                return encryptedData;
            }
            
            public byte[] DecryptStrong(byte[] key, byte[] data)
            {
                // Split the nonce (IV) and ciphertext
                var nonce = data.Take(12).ToArray();
                var ciphertext = data.Skip(12).ToArray();

                // Create an AES-GCM cipher with BouncyCastle
                var cipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
                var parameters = new ParametersWithIV(new KeyParameter(key), nonce);
                cipher.Init(false, parameters);

                // Decrypt the data
                var decryptedData = cipher.DoFinal(ciphertext);

                return decryptedData;
            }
            
            [Benchmark]
            public void Serialize_NoEncryption()
            {

                var packet = CCharacterLoadedPacket.Create(1);
                
                using var memoryStream = new MemoryStream();
            
                Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
            }
            
            [Benchmark]
            public void Serialize_Aes256_Weak()
            {

                var packet = CCharacterListPacket.Create(1, Encrypt);
                
                using var memoryStream = new MemoryStream();
            
                Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
            }
            
            [Benchmark]
            public void Serialize_Aes128()
            {
                var packet = CCharacterListPacket.Create(1, bytes => EncryptStrong(_serverSharedKey, bytes));
                
                using var memoryStream = new MemoryStream();
            
                Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
            }
            
            [Benchmark]
            public void Deserialize_Aes256_Weak()
            {
                _encryptedPacket_Aes256_Weak.Seek(0, SeekOrigin.Begin);
                var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_encryptedPacket_Aes256_Weak, PrefixStyle.Base128);
                
                var decryptedBytes = Decrypt(packet.Payload);
                
                using var memoryStream = new MemoryStream(decryptedBytes);
                
                var innerPacket = Serializer.Deserialize<CCharacterLoadedPacket>(memoryStream);
                if (innerPacket is not { AccountId: 1 })
                {
                    throw new Exception("Failed to deserialize packet");
                }
            }
            
            [Benchmark]
            public void Deserialize_Aes128()
            {
                _encryptedPacket_Aes128.Seek(0, SeekOrigin.Begin);
                var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_encryptedPacket_Aes128, PrefixStyle.Base128);
                
                var decryptedBytes = DecryptStrong(_clientSharedKey, packet.Payload);
                
                using var memoryStream = new MemoryStream(decryptedBytes);
                
                var innerPacket = Serializer.Deserialize<CCharacterListPacket>(memoryStream);
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

        class SimulatedClient
        {
            private readonly string _username;
            private readonly string _password;
            private readonly AvalonTcpClient _tcp;
            private readonly AvalonUdpClient _udp;

            private int _accountId;
            private int _characterId;

            private volatile bool _isLogged;
            private volatile bool _needsFirstCharacter;
            private volatile bool _characterSelected;
            
            public SimulatedClient(string username, string password)
            {
                _username = username;
                _password = password;
                _tcp = new AvalonTcpClient(new AvalonTcpClientSettings
                {
                    Host = "85.246.140.89",
                    Port = 21000,
                    CertificatePath = "cert-public.pem"
                });
                _udp = new AvalonUdpClient(new AvalonUdpClientSettings
                {
                    Host = "85.246.140.89",
                    Port = 21000,
                });
                
                _tcp.AuthResult += OnAuthResult;
                _tcp.CharacterList += OnCharacterList;
                _tcp.CharacterCreated += OnCharacterCreated;
                _tcp.CharacterSelected += OnCharacterSelected;
                
                _udp.AuthResult += OnAuthResult;
            }

            private void OnCharacterSelected(object sender, SCharacterSelectedPacket packet)
            {
                _characterSelected = true;
            }

            private async void OnCharacterCreated(object sender, SCharacterCreatedPacket packet)
            {
                await _tcp.SendCharacterListPacket(_accountId);
            }

            private async void OnCharacterList(object sender, SCharacterListPacket packet)
            {
                if (packet.CharacterCount == 0)
                {
                    _needsFirstCharacter = true;
                }
                else
                {
                    // login with the first character
                    _characterId = packet.Characters[0].CharacterId;
                    await _tcp.SendCharacterSelectedPacket(_accountId, _characterId);
                }
            }

            private async void OnAuthResult(object sender, SAuthResultPacket packet)
            {
                switch (packet.Result)
                {
                    case AuthResult.WRONG_KEY:
                        Console.WriteLine("Wrong session private key");
                        break;
                    case AuthResult.INVALID_CREDENTIALS:
                        Console.WriteLine("Invalid username or password");    
                        break;
                    case AuthResult.PENDING_KEY:
                        _udp.SetPrivateKey(packet.PrivateKey);
                        await _udp.SendAuthPatchPacket(packet.AccountId);
                        break;
                    case AuthResult.SUCCESS:
                        _tcp.AccountId = packet.AccountId;
                        _udp.AccountId = packet.AccountId;
                        _accountId = packet.AccountId;
                        _isLogged = true;
                        break;
                }
            }

            public async void Run()
            {
                await _tcp.ConnectAsync();
                await _udp.ConnectAsync();
                
                await _tcp.SendAuthPacket(_username, _password);

                while (true)
                {
                    if (_isLogged)
                    {
                        _isLogged = false;
                        await _tcp.SendCharacterListPacket(_accountId);
                    }
                    
                    if (_needsFirstCharacter)
                    {
                        _needsFirstCharacter = false;
                        // use username as character name
                        await _tcp.SendCharacterCreatePacket(_accountId, _username, 1);
                    }

                    if (_characterSelected)
                    {
                        _characterSelected = false;
                        await _tcp.SendCharacterLoadedPacket();
                    }
                }
            }
        }
    }
}
