using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Auth.Database.Repositories;
using Avalon.Common.ValueObjects;
using Avalon.Hosting;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Server.Auth;

public interface IAuthConnection : IConnection
{
    public AccountId? AccountId { get; set; }
    
    byte[] GenerateHandshakeData();
    bool VerifyHandshakeData(byte[] handshakeData);
    
    AuthServer Server { get; }
}

public class AuthConnection : Connection, IAuthConnection
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    public AccountId? AccountId { get; set; }
    public new AuthServer Server { get; }
    
    private byte[] _handshakeData = [];
    
    public AuthConnection(IServerBase server, TcpClient client, ILoggerFactory loggerFactory,
        IPacketReader packetReader, IServiceScopeFactory serviceScopeFactory)
        : base(loggerFactory.CreateLogger<AuthConnection>(), server, packetReader)
    {
        _serviceScopeFactory = serviceScopeFactory;
        Server = (server as AuthServer)!;
        Init(client);
    }

    protected override void OnHandshakeFinished()
    {
        Server.CallConnectionListener(this);
    }
    
    public byte[] GenerateHandshakeData()
    {
        _handshakeData = CryptoSession.GenerateHandshakeData();
        return _handshakeData;
    }

    public bool VerifyHandshakeData(byte[] handshakeData)
    {
        return _handshakeData.SequenceEqual(handshakeData);
    }

    protected override async Task<Stream> GetStream(TcpClient client)
    {
        var networkStream = new NetworkStream(client.Client);
        var sslStream = new SslStream(networkStream, false, OnClientCertificateValidation);

        var certificate = Server.Certificate;
        
        await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);
        
        return sslStream;
    }

    protected override async Task OnClose(bool expected = true)
    {
        await SaveDisconnectedAccountAsync();
        await Server.RemoveConnection(this);
    }
    
    private async Task SaveDisconnectedAccountAsync()
    {
        if (AccountId == null) return;
        
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var accountRepository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        
        var account = await accountRepository.FindByIdAsync(AccountId);
        if (account != null)
        {
            account.Online = false;
            account.TotalTime += (int) (DateTime.UtcNow - account.LastLogin).TotalSeconds;
            await accountRepository.UpdateAsync(account);
        }
    }

    protected override async Task OnReceive(NetworkPacket packet, Packet? payload)
    {
        await Server.CallListener(this, packet, payload);
    }

    protected override long GetServerTime()
    {
        return Server.ServerTime;
    }
    
    private bool OnClientCertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        return true;
    }
}
