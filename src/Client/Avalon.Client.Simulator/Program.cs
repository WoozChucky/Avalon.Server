using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Tcp;
using Avalon.Network.Udp;

namespace Avalon.Client.Simulator
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
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
        }
        

        public class SimulatedClient
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
                    Host = "nunolevezinho.xyz",
                    Port = 21000,
                    CertificatePath = "cert-public.pem"
                });
                _udp = new AvalonUdpClient(new AvalonUdpClientSettings
                {
                    Host = "nunolevezinho.xyz",
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
