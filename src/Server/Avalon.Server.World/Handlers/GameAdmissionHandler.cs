using Avalon.Common.Cryptography;
using Avalon.Common.GameAuth;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Avalon.World.GameAuth;
using Avalon.World.Persistence;

namespace Avalon.Server.World.Handlers;

/// <summary>
/// Redeems a join ticket and admits the connection. On a world that seals (<c>Network:PacketEncryption</c>), admission
/// also completes the key exchange: the client's public key must be one, the session is keyed from it, and the reply
/// carries the connection's own. A world that does not seal runs no exchange (#878): whatever public key the client
/// sends (a 0.3.0 client sends one before it knows the mode) is ignored, and the reply's key is empty.
/// </summary>
/// <remarks>
/// The session is keyed off the tick, after the ticket is redeemed (an agreement, two derivations and two ciphers, about
/// 0.14 ms); the tick only publishes the admission and sends the reply. Keying first is safe: the read loop opens nothing
/// with the session until it reads Ready, which is written after both ciphers; the reply that carries the server's key,
/// the only way a client can seal, goes out after the keying has finished; and a packet that arrives before the
/// admission is published is dropped, since the connection has no lease yet.
/// <para>
/// One source may have at most <c>World:Admission:MaxInFlightPerSource</c> admissions in flight, its IPv6 /56 at most
/// <c>MaxInFlightPerIPv6Block</c>, and it may start at most <c>MaxPerSourcePerMinute</c> a minute, its /56 at most
/// <c>MaxPerIPv6BlockPerMinute</c> (#1052,
/// <see cref="AdmissionSourceBudget" />): one past any of them is answered <c>ServiceUnavailable</c> and closed before
/// its ticket is redeemed. Its slot is taken after the checks that cost nothing and given back exactly once: at once when the
/// admission lane refuses the redemption, or else when the redemption has ended (<see cref="KeyAsync" />, which reads
/// its end whatever it was: an answer, a fault, a cancellation). A connection that closes first gives it back no
/// sooner: its redemption still runs, and still costs the identity service, until it ends, which the admission
/// client's own timeout bounds.
/// </para>
/// <para>
/// The world's backstop (#967, <see cref="WorldBackstop" />) runs inside the redemption's own work, after identity
/// activated the session: an admission over the world's player limit plus its margin, from an account that is not staff,
/// has its lease revoked and its session ended as <c>backstop</c> (which removes its place), and is answered
/// <c>WorldFull</c>. Its answer is the redemption's, so the task the connection tracks says "no lease" (a close's drain
/// ends nothing a second time) and the slot is given back once, in <see cref="KeyAsync" />, as before.
/// </para>
/// </remarks>
public sealed class GameAdmissionHandler(IGameAdmissionClient admission, AdmissionSourceBudget sources, WorldBackstop backstop,
    IWorldServer server) : IWorldPacketHandler<CGameAdmissionPacket>
{
    public Task ExecuteAsync(WorldPacketContext<CGameAdmissionPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection is not WorldConnection connection || !connection.IsTlsAuthenticated ||
            !GameAuthCryptography.IsToken(ctx.Packet.JoinTicket) ||
            (connection.KeyExchange is { } exchange && ctx.Packet.PublicKey.Length != exchange.ServerCrypto.GetValidKeySize()) ||
            !connection.TryBeginAdmission())
        {
            ctx.Connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, GameAdmissionResult.InvalidRequest));
            return ctx.Connection.CloseAsync(false);
        }
        if (sources.TryAcquire(connection.ClientIPAddress, out AdmissionSourceBudget.Slot slot) is var refusal and
            not AdmissionSourceRefusal.None)
        {
            // Its source is over its budget (#1052): in flight, its IPv6 /56's in flight, or its rate. Answered as a full
            // lane is, before the ticket is redeemed, on the receive path. No wire change: the client is told to try later.
            connection.ReportAdmissionOverSourceBudget(refusal, refusal switch
            {
                AdmissionSourceRefusal.Rate => sources.MaxPerSourcePerMinute,
                AdmissionSourceRefusal.IPv6BlockRate => sources.MaxPerIPv6BlockPerMinute,
                AdmissionSourceRefusal.IPv6BlockInFlight => sources.MaxInFlightPerIPv6Block,
                _ => sources.MaxInFlightPerSource,
            });
            connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, GameAdmissionResult.ServiceUnavailable));
            return connection.CloseAsync(false);
        }
        string ticket = ctx.Packet.JoinTicket;
        Task<WorldAdmissionResult> redeemed = WorldDatabaseWork.Admission.Run(() => AdmitAsync(connection, ticket, token));
        if (WorldDatabaseWork.IsRefusal(redeemed))
        {
            slot.Release();
            // The admission work queue is full (#1050): nothing was started, so nothing can be live. Answered here, on
            // the receive path, as the refusals above are: no exception read, nothing tracked, nothing for the tick.
            connection.ReportAdmissionOverloaded();
            connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, GameAdmissionResult.ServiceUnavailable));
            return connection.CloseAsync(false);
        }
        byte[] clientKey = connection.KeyExchange is null ? [] : ctx.Packet.PublicKey.ToArray();
        connection.TrackAdmission(redeemed);
        connection.EnqueueContinuation(KeyAsync(redeemed, connection, clientKey, slot), outcome =>
        {
            WorldAdmissionResult reply = outcome.Reply;
            if (!connection.IsConnected || connection.IsClosing) return;
            if (reply.Lease is null || !reply.Lease.IsActive)
            {
                connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, Refusal(reply.Error)));
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
                connection.Close(false);
#pragma warning restore MA0045
                return;
            }
            if (outcome.Keying != Keying.Done)
            {
                if (outcome.Keying == Keying.BadClientKey)
                {
                    connection.ReportBadPublicKey(reply.Lease.Authority.AccountId.Value);
                    connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, GameAdmissionResult.InvalidRequest));
                }
                else
                {
                    connection.ReportKeyingFailed(outcome.Error!);
                }
                reply.Lease.Revoke();
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
                connection.Close(false);
#pragma warning restore MA0045
                return;
            }
            try
            {
                connection.PublishAdmission(reply.Lease);
                connection.Send(SGameAdmissionPacket.Create(connection.KeyExchange?.ServerCrypto.GetPublicKey() ?? [], PacketEncoder.Shared,
                    GameAdmissionResult.Accepted, connection.PacketEncryption));
            }
            catch (Exception error)
            {
                connection.ReportAdmissionFailed(error);
                reply.Lease.Revoke();
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
                connection.Close(false);
#pragma warning restore MA0045
            }
        });
        return Task.CompletedTask;
    }

    private enum Keying { Done, BadClientKey, Failed }

    private readonly record struct KeyedAdmission(WorldAdmissionResult Reply, Keying Keying, Exception? Error);

    /// <summary>
    /// What a redemption that ended without an answer is taken for (#1050): the identity service's own "try later",
    /// which the continuation answers <c>ServiceUnavailable</c> and closes. Nothing in it is live: a redemption that
    /// faulted handed back no lease, and the admission client ends a session it reserved before it lets a fault through.
    /// </summary>
    private static readonly WorldAdmissionResult s_unredeemed = new(null, GameAuthErrors.ServiceUnavailable);

    /// <summary>
    /// The refusal a redemption without an active lease is answered with. The identity service's answers that mean
    /// "try later" (unreachable or overloaded, a session barrier still pending, the world in maintenance or without a
    /// fresh heartbeat) are <c>ServiceUnavailable</c>; a client too old for this world or for the API
    /// (<c>CLIENT_OUTDATED</c>, <c>UNSUPPORTED_PROTOCOL</c>, #1053) is <c>UnsupportedClient</c>; every other is
    /// <c>AuthorizationRequired</c>: the context not allowed on this world (<c>WORLD_NOT_ALLOWED</c>), revoked, its
    /// account's standing, the character not the account's among them. A world over its player limit
    /// (<c>WORLD_FULL</c>, the backstop, #967) is <c>WorldFull</c>.
    /// </summary>
    private static GameAdmissionResult Refusal(string? error) => error switch
    {
        GameAuthErrors.ServiceUnavailable or GameAuthErrors.BarrierPending or GameAuthErrors.WorldUnavailable
            => GameAdmissionResult.ServiceUnavailable,
        GameAuthErrors.ClientOutdated or GameAuthErrors.UnsupportedProtocol => GameAdmissionResult.UnsupportedClient,
        GameAuthErrors.WorldFull => GameAdmissionResult.WorldFull,
        _ => GameAdmissionResult.AuthorizationRequired,
    };

    /// <summary>
    /// Off the tick, on the admission lane: identity's redemption and activation, then the world's backstop (#967). A
    /// refused admission's lease is revoked and its session ended here as <c>backstop</c>, which removes its place at
    /// identity (not the abandon of a failed admission, whose end carries no reason and would hold it); the result then
    /// carries no lease, so the continuation answers <c>WorldFull</c> and a close's drain ends nothing again. The end
    /// holds the admission lane and the source's slot, so it is bounded as a failed admission's abandon is
    /// (<see cref="WorldBackstop.EndTimeout" />, 2 s); one that fails or times out leaves the session to run out with its
    /// lease (within 45 s), and its place with it.
    /// </summary>
    private async Task<WorldAdmissionResult> AdmitAsync(WorldConnection connection, string ticket, CancellationToken token)
    {
        WorldAdmissionResult result = await admission.AdmitAsync(ticket, connection.Id, Guid.NewGuid(), token);
        if (result.Lease is not { IsActive: true } lease || !backstop.Refuses(lease, server.Connections, connection)) return result;
        lease.Revoke();
        using (CancellationTokenSource bound = backstop.EndDeadline())
            await admission.EndAsync(lease, GameSessionEndReasons.Backstop, bound.Token);
        return new WorldAdmissionResult(null, GameAuthErrors.WorldFull);
    }

    /// <summary>
    /// Gives back the admission's source slot once the redemption has ended, whatever its end (#1052), then keys the
    /// session, on a world that seals, once the ticket is redeemed for an active lease: off the tick, on the thread the
    /// redemption finished on. Never faults: a redemption that faulted or was cancelled once started is
    /// reported where it ended, read without a throw, and taken for <see cref="s_unredeemed" />; a keying that fails is
    /// handed to the continuation. The redemption's own task, which the connection tracks so a drain can end a lease
    /// that was never published, stays the one that says what was redeemed.
    /// </summary>
    private static async Task<KeyedAdmission> KeyAsync(Task<WorldAdmissionResult> redeemed, WorldConnection connection,
        byte[] clientKey, AdmissionSourceBudget.Slot slot)
    {
        await ((Task)redeemed).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        slot.Release();
        if (!redeemed.IsCompletedSuccessfully)
        {
            connection.ReportRedemptionFailed(redeemed.Exception?.InnerException ?? new TaskCanceledException(redeemed));
            return new KeyedAdmission(s_unredeemed, Keying.Done, null);
        }
        WorldAdmissionResult reply = redeemed.Result;
        ConnectionKeyExchange? keys = connection.KeyExchange;
        if (keys is null || reply.Lease is not { IsActive: true })
            return new KeyedAdmission(reply, Keying.Done, null);
        try
        {
            keys.Session.Initialize(clientKey);
            return new KeyedAdmission(reply, Keying.Done, null);
        }
        catch (InvalidPublicKeyException)
        {
            return new KeyedAdmission(reply, Keying.BadClientKey, null);
        }
        catch (Exception error)
        {
            return new KeyedAdmission(reply, Keying.Failed, error);
        }
    }
}
