using System.Globalization;
using Avalon.Common.Utils;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

/// <summary>
/// The version handshake, once per admitted connection. A client older than the world accepts (below the world's
/// <c>MinVersion</c>, or with a version that does not parse) is told so: the reply carries
/// <see cref="WorldHandshakeRefusal.ClientVersionTooOld" /> and the oldest version the world accepts, and the connection
/// is closed after it (the close flushes the outbox, so the reply goes out first). Every other refusal (no active
/// admission, a second handshake, a world whose own <c>MinVersion</c> does not parse) closes without a reply. The world
/// keeps no floor of its own (#949): the API's sign-in floor (<c>Application:GameWorkloads:MinClientVersion</c>) gates
/// the version the client stated at sign-in, not the one it states here, and this handshake compares only against the
/// world's own <c>MinVersion</c>. The two are separate values; binding them would need the context's version carried to
/// the world in the redemption receipt, which it is not.
/// </summary>
public sealed partial class WorldHandshakeHandler(IWorld world, ILogger<WorldHandshakeHandler> logger)
    : IWorldPacketHandler<CWorldHandshakePacket>
{
    public Task ExecuteAsync(WorldPacketContext<CWorldHandshakePacket> ctx, CancellationToken token = default)
    {
        // The account comes from the lease, read once: the admission publishes the lease before it sets the
        // connection's AccountId, and this runs on the read loop, which can land in between.
        if (ctx.Connection is not WorldConnection connection || connection.GameSessionLease is not { IsActive: true } lease ||
            connection.IsProtocolAccepted || !SemVerPacker.TryPack(world.MinVersion, out uint required))
        {
            return ctx.Connection.CloseAsync(false);
        }

        long accountId = lease.Authority.AccountId.Value;
        bool parsed = SemVerPacker.TryPack(ctx.Packet.Version, out uint version);
        if (!parsed || version < required)
        {
            // Canonical, as compared: the configured string may carry what the parser forgives (spaces, zeros, a sign).
            string requiredVersion = Format(required);
            // The client's version as it parsed, never the string it sent: that is the peer's to choose.
            Log.ClientVersionTooOld(logger, connection.Id, accountId, parsed ? Format(version) : "unparsable",
                requiredVersion);
            connection.Send(SWorldHandshakePacket.CreateRefusal(accountId, WorldHandshakeRefusal.ClientVersionTooOld,
                requiredVersion, PacketEncoder.Shared));
            return connection.CloseAsync(false);
        }

        if (!connection.AcceptProtocol())
            return connection.CloseAsync(false);

        connection.Send(SWorldHandshakePacket.Create(accountId, true, PacketEncoder.Shared));
        connection.RequestInitialTimeSyncPing();
        return Task.CompletedTask;
    }

    private static string Format(uint version) =>
        string.Create(CultureInfo.InvariantCulture, $"{version >> 24}.{(version >> 16) & 0xFF}.{version & 0xFFFF}");

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Refused the version handshake of connection {ConnectionId} of account {AccountId}: client version {ClientVersion} is older than the world's minimum {RequiredVersion}")]
        public static partial void ClientVersionTooOld(ILogger logger, Guid connectionId, long accountId,
            string clientVersion, string requiredVersion);
    }
}
