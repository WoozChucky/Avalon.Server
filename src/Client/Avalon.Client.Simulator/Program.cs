using System.Security.Cryptography;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Tcp;
using Avalon.Network.Udp;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Avalon.Client.Simulator
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
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
        [RPlotExporter]
        public class MemoryBenchmark
        {
            private Aes _aes;

            private byte[] Encrypt(byte[] data)
            {
                using var memoryStream = new MemoryStream();

                using (var encryptor = _aes.CreateEncryptor())
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

                using (var decryptor = _aes.CreateDecryptor())
                {
                    using (var csDecrypt = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Write))
                    {
                        csDecrypt.Write(data, 0, data.Length);
                        csDecrypt.FlushFinalBlock();
                    }
                }

                return memoryStream.ToArray();
            }
            
            [GlobalSetup]
            public void Setup()
            {
                var privateKey = new byte[32]; // 256 bits = 32 bytes
                using (var rng = new RNGCryptoServiceProvider())
                {
                    rng.GetBytes(privateKey);
                }
                
                _aes = Aes.Create();
                _aes.Key = privateKey;
                _aes.IV = new byte[] {0x5A, 0x36, 0x7F, 0x8D, 0xE9, 0x02, 0xC4, 0xAF, 0x71, 0x5E, 0x9B, 0x44, 0xD7, 0x1A, 0x80, 0x3F};
            }
            
            [Params(1, 10000)]
            public int N;
            
            [Benchmark]
            public void Serialize_Aes()
            {

                var packet = CCharacterListPacket.Create(N, Encrypt);
                
                using var memoryStream = new MemoryStream();
            
                Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
            }
            
            [Benchmark]
            public void Serialize_NoEncryption()
            {

                var packet = CCharacterLoadedPacket.Create(N);
                
                using var memoryStream = new MemoryStream();
            
                Serializer.SerializeWithLengthPrefix(memoryStream, packet, PrefixStyle.Base128);
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
