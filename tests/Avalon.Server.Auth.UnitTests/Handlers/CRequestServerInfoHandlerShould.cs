using Avalon.Common.Cryptography;
using Avalon.Common.Utils;
using Avalon.Network.Packets.Handshake;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CRequestServerInfoHandlerShould
{
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();
    private readonly ICryptoManager _serverCrypto = Substitute.For<ICryptoManager>();

    private static CRequestServerInfoHandler CreateHandler(string minClientVersion = "0.0.1", string serverVersion = "1.0.0")
    {
        IOptions<AuthConfiguration> options = Options.Create(new AuthConfiguration
        {
            MinClientVersion = minClientVersion,
            ServerVersion = serverVersion
        });
        return new CRequestServerInfoHandler(NullLoggerFactory.Instance, options);
    }

    public CRequestServerInfoHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _serverCrypto.GetPublicKey().Returns(new byte[32]);
        _connection.ServerCrypto.Returns(_serverCrypto);
        _connection.Id.Returns(Guid.NewGuid());
    }

    private AuthPacketContext<CRequestServerInfoPacket> Ctx(string clientVersion) =>
        new() { Packet = new CRequestServerInfoPacket { ClientVersion = clientVersion }, Connection = _connection };

    // ── rejection cases ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0.0.0")]          // below minimum
    [InlineData("")]               // empty
    [InlineData("not-a-version")]  // unparseable
    public async Task SendRejectionPacket_AndCloseConnection_WhenClientVersionIsTooOldOrInvalid(string clientVersion)
    {
        CRequestServerInfoHandler handler = CreateHandler(minClientVersion: "1.0.0", serverVersion: "1.0.0");
        OutboundPacket sent = default;
        _connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent = ci.Arg<OutboundPacket>());

        await handler.ExecuteAsync(Ctx(clientVersion));

        _connection.Received(1).Close();
        Assert.NotNull(sent.Payload);
        SServerInfoPacket packet = Serializer.Deserialize<SServerInfoPacket>(new MemoryStream(sent.PayloadMemory.ToArray()));
        Assert.Equal(ServerInfoResult.ClientVersionTooOld, packet.Result);
    }

    [Fact]
    public async Task SendRejectionPacket_BeforeClosingConnection_WhenClientVersionIsTooOld()
    {
        CRequestServerInfoHandler handler = CreateHandler(minClientVersion: "1.0.0");
        bool packetSentBeforeClose = false;
        bool packetSent = false;

        _connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(_ => packetSent = true);
        _connection.When(c => c.Close()).Do(_ => packetSentBeforeClose = packetSent);

        await handler.ExecuteAsync(Ctx("0.9.0"));

        Assert.True(packetSentBeforeClose, "Rejection packet must be sent before Close() is called");
    }

    [Fact]
    public async Task IncludeServerVersion_InRejectionPacket()
    {
        CRequestServerInfoHandler handler = CreateHandler(minClientVersion: "2.0.0", serverVersion: "3.1.4");
        OutboundPacket sent = default;
        _connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent = ci.Arg<OutboundPacket>());

        await handler.ExecuteAsync(Ctx("1.0.0"));

        Assert.NotNull(sent.Payload);
        SServerInfoPacket packet = Serializer.Deserialize<SServerInfoPacket>(new MemoryStream(sent.PayloadMemory.ToArray()));
        Assert.Equal(SemVerPacker.Pack("3.1.4"), packet.ServerVersion);
        Assert.Equal(ServerInfoResult.ClientVersionTooOld, packet.Result);
    }

    // ── success cases ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0.0.1")]   // the minimum itself
    [InlineData("2.0.0")]   // newer than the minimum
    public async Task SendServerInfo_WhenClientVersionIsAtLeastTheMinimum(string clientVersion)
    {
        CRequestServerInfoHandler handler = CreateHandler(minClientVersion: "0.0.1");
        await handler.ExecuteAsync(Ctx(clientVersion));

        _connection.DidNotReceive().Close();
        _connection.Received(1).Send(Arg.Any<OutboundPacket>());
        _serverCrypto.Received(1).GetPublicKey();
    }

    [Fact]
    public async Task SendSemverPackedServerVersion_InServerInfoPacket()
    {
        CRequestServerInfoHandler handler = CreateHandler(minClientVersion: "1.2.3", serverVersion: "2.5.10");
        OutboundPacket sent = default;
        _connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent = ci.Arg<OutboundPacket>());

        await handler.ExecuteAsync(Ctx("1.2.3"));

        Assert.NotNull(sent.Payload);
        SServerInfoPacket packet = Serializer.Deserialize<SServerInfoPacket>(new MemoryStream(sent.PayloadMemory.ToArray()));
        // 2.5.10 = (2 << 24) | (5 << 16) | 10 = 0x02_05_000A = 33,882,122
        Assert.Equal((2u << 24) | (5u << 16) | 10u, packet.ServerVersion);
        Assert.Equal(ServerInfoResult.Success, packet.Result);
    }
}
