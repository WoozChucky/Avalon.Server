using System.Diagnostics.Metrics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Server.Auth;

public interface IAuthConnection : IConnection
{
    public AccountId? AccountId { get; set; }

    AuthServer Server { get; }

    byte[] GenerateHandshakeData();
    bool VerifyHandshakeData(byte[] handshakeData);
}

public class AuthConnection : Connection, IAuthConnection
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private ObservableGauge<double> _bytesReceivedRate;
    private ObservableGauge<double> _bytesSentRate;

    private byte[] _handshakeData = [];

    private ObservableGauge<double> _packetReceivedRate;
    private ObservableGauge<double> _packetSentRate;

    public AuthConnection(IServerBase server, TcpClient client, ILoggerFactory loggerFactory,
        IPacketReader packetReader, IServiceScopeFactory serviceScopeFactory)
        : base(loggerFactory.CreateLogger<AuthConnection>(), server, packetReader)
    {
        _serviceScopeFactory = serviceScopeFactory;
        Server = (server as AuthServer)!;
        Init(client);

        _packetSentRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.out.packets.rate",
            () => PacketSentRate, "packets/s", "Rate of packets sent");
        _packetReceivedRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.in.packets.rate",
            () => PacketReceivedRate, "packets/s", "Rate of packets received");
        _bytesSentRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.out.bytes.rate",
            () => BytesSentRate, "bytes/s", "Rate of bytes sent");
        _bytesReceivedRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.in.bytes.rate",
            () => BytesReceivedRate, "bytes/s", "Rate of bytes received");
    }

    public AccountId? AccountId { get; set; }
    public new AuthServer Server { get; }

    public byte[] GenerateHandshakeData()
    {
        _handshakeData = CryptoSession.GenerateHandshakeData();
        return _handshakeData;
    }

    public bool VerifyHandshakeData(byte[] handshakeData) => _handshakeData.SequenceEqual(handshakeData);

    public override void Send(NetworkPacket packet)
    {
        DiagnosticsConfig.Auth.BytesSent.Add(packet.Size);
        DiagnosticsConfig.Auth.PacketsSent.Add(1);
        base.Send(packet);
    }

    protected override void OnHandshakeFinished() => Server.CallConnectionListener(this);

    protected override async Task<PacketStream> GetStream(TcpClient client)
    {
        NetworkStream networkStream = new(client.Client);
        SslStream sslStream = new(networkStream, false, OnClientCertificateValidation);

        X509Certificate2 certificate = Server.Certificate;

        await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);

        return new PacketStream(sslStream);
    }

    protected override async Task OnClose(bool expected = true)
    {
        await SaveDisconnectedAccountAsync();
        await Server.RemoveConnection(this);
    }

    private async Task SaveDisconnectedAccountAsync()
    {
        if (AccountId == null)
        {
            return;
        }

        await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
        IAccountRepository accountRepository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();

        Account? account = await accountRepository.FindByIdAsync(AccountId);
        if (account != null)
        {
            account.Online = false;
            account.TotalTime += (int)(DateTime.UtcNow - account.LastLogin).TotalSeconds;
            await accountRepository.UpdateAsync(account);
        }
    }

    protected override async Task OnReceive(NetworkPacketHeader header, Packet? payload)
    {
        await Server.CallListener(this, header, payload);
    }

    protected override void OnPacketAccounted(int size)
    {
        DiagnosticsConfig.Auth.BytesReceived.Add(size);
        DiagnosticsConfig.Auth.PacketsReceived.Add(1);
    }


    protected override long GetServerTime() => Server.ServerTime;

    private bool OnClientCertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain,
        SslPolicyErrors sslpolicyerrors) => true;
}
