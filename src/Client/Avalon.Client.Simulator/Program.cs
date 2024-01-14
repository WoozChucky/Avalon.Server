using System.Security.Cryptography;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Tcp;

namespace Avalon.Client.Simulator
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
            const int clientCount = 5; // This is the number of clients to simulate

            const string username = "test";
            const string password = "test";

            for (var i = 0; i < clientCount; i++)
            {
                // how create a thread for each client and run them
                var client = new SimulatedClient($"{username}{i}", $"{password}{i}");
                await Task.Delay(RandomNumberGenerator.GetInt32(2500, 5000));
                await Task.Run(() => client.Run());
                await Task.Delay(RandomNumberGenerator.GetInt32(2500, 5000));
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

            private int _accountId;
            private int _characterId;

            private volatile bool _isLogged;
            private volatile bool _needsFirstCharacter;
            private volatile bool _characterSelected;
            private volatile bool _needsAccount;
            private volatile bool _inGame;

            private volatile float _currentXPosition;
            private volatile float _currentYPosition;
            private volatile float _currentXVelocity;
            private volatile float _currentYVelocity;
            // Initialize persistent direction
            private volatile float persistentXDirection = 0f;
            private volatile float persistentYDirection = 0f;
            
            // Define persistence factor (higher values mean less frequent direction changes)
            private float _persistence = 0.8f;

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
                
                _tcp.AuthResult += OnAuthResult;
                _tcp.CharacterList += OnCharacterList;
                _tcp.CharacterCreated += OnCharacterCreated;
                _tcp.CharacterSelected += OnCharacterSelected;
                _tcp.RegisterResult += OnRegisterResult;
                
            }

            private async void OnRegisterResult(object sender, SRegisterResultPacket packet)
            {
                switch (packet.Result)
                {
                    case RegisterResult.Ok:
                        await _tcp.SendAuthPacket(_username, _password);
                        break;
                    case RegisterResult.EmptyUsername:
                        Console.WriteLine("Empty username");
                        break;
                    case RegisterResult.EmptyPassword:
                        Console.WriteLine("Empty password");
                        break;
                    case RegisterResult.PasswordTooShort:
                        Console.WriteLine("Password too short");
                        break;
                    case RegisterResult.PasswordTooLong:
                        Console.WriteLine("Password too long");
                        break;
                    case RegisterResult.UnknownError:
                        Console.WriteLine("Unknown error");
                        break;
                }
            }

            private void OnCharacterSelected(object sender, SCharacterSelectedPacket packet)
            {
                _currentXPosition = packet.Character.X;
                _currentYPosition = packet.Character.Y;
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
                    await _tcp.SendCharacterSelectedPacket(_characterId);
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
                        _needsAccount = true;
                        break;
                    case AuthResult.SUCCESS:
                        _tcp.AccountId = packet.AccountId;
                        _accountId = packet.AccountId;
                        _isLogged = true;
                        break;
                }
            }

            public async void Run()
            {
                await _tcp.ConnectAsync();
                
                await Task.Delay(RandomNumberGenerator.GetInt32(1500, 3000));
                
                await _tcp.SendAuthPacket(_username, _password);

                while (true)
                {
                    if (_needsAccount)
                    {
                        _needsAccount = false;
                        await _tcp.SendRegisterPacket(_username, _password);
                    }
                    
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
                        _inGame = true;
                    }

                    if (_inGame)
                    {
                        if (RandomNumberGenerator.GetInt32(0, 10) == 0)
                        {
                            // Introduce acceleration and deceleration
                            float acceleration = 0.1f;
                            float deceleration = 0.2f;

                            // Random acceleration changes
                            float xAcceleration = RandomFloat(-acceleration, acceleration);
                            float yAcceleration = RandomFloat(-acceleration, acceleration);

                            // Update velocity with acceleration
                            _currentXVelocity += xAcceleration;
                            _currentYVelocity += yAcceleration;

                            // Apply deceleration to simulate natural slowing down
                            _currentXVelocity *= (1 - deceleration);
                            _currentYVelocity *= (1 - deceleration);

                            // Update position based on velocity
                            _currentXPosition = Clamp(_currentXPosition + (int)_currentXVelocity, 0, 250);
                            _currentYPosition = Clamp(_currentYPosition + (int)_currentYVelocity, 0, 250);

                            // Apply persistence to maintain direction
                            if (RandomFloat(0, 1) > _persistence)
                            {
                                // Change persistent direction
                                persistentXDirection = RandomFloat(-1, 1);
                                persistentYDirection = RandomFloat(-1, 1);
                            }

                            // Update velocity based on persistent direction
                            _currentXVelocity = persistentXDirection;
                            _currentYVelocity = persistentYDirection;

                            // Broadcast the movement updates
                            await _tcp.BroadcastMovementUpdates(0f, _currentXPosition, _currentYPosition, _currentXVelocity, _currentYVelocity);

                            // Introduce a small delay to simulate more natural movement
                            Thread.Sleep(50);
                        }
                        
                    }
                }
            }
            
            float RandomFloat(float min, float max)
            {
                return min + (float)RandomNumberGenerator.GetInt32((int)((min * 1000)), (int)((max * 1000) + 1)) / 1000f;
            }
            
            // Helper function to clamp a value within a specified range
            float Clamp(float value, int min, int max)
            {
                return Math.Max(min, Math.Min(max, value));
            }
        }
    }
}
