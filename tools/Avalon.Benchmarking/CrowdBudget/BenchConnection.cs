using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;

namespace Avalon.Benchmarking.CrowdBudget;

/// <summary>
/// A connection that only counts what it is sent, and holds the character, dialogue and target a tick
/// reads. Allocation-free, so the tick paths it serves can be pinned at zero bytes. Everything else throws.
/// </summary>
internal sealed class BenchConnection(CharacterEntity character) : IWorldConnection
{
    public int Sent { get; private set; }

    public ICharacter? Character { get; set; } = character;

    public (ObjectGuid Npc, DialogueNodeId Node)? CurrentDialogue { get; set; }

    public IAvalonCryptoSession CryptoSession { get; } = new PassThroughCryptoSession();

    public void Send(NetworkPacket packet) => Sent++;

    public Guid Id => throw new NotSupportedException();
    public Task? ExecuteTask => throw new NotSupportedException();
    public string RemoteEndPoint => throw new NotSupportedException();
    public ICryptoManager ServerCrypto => throw new NotSupportedException();
    public AccountId? AccountId { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public PendingSpawn? PendingSpawn => throw new NotSupportedException();
    public bool SelectInProgress => throw new NotSupportedException();
    public long SelectStartedTicks => throw new NotSupportedException();
    public bool IsConnected => true;
    public bool IsClosing => false;
    public long Latency => throw new NotSupportedException();
    public long RoundTripTime => throw new NotSupportedException();
    public long CurrentPacketArrivedTicks => throw new NotSupportedException();
    public bool InGame => throw new NotSupportedException();
    public bool InMap => throw new NotSupportedException();
    public uint LastInputSeq { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public ulong? CurrentTargetGuid { get; set; }
    public AccountLocale Locale { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public AccountAccessLevel AccessLevel => throw new NotSupportedException();
    public bool RespawnInFlight { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public void Close(bool expected = true) => throw new NotSupportedException();
    public Task CloseAsync(bool expected = true) => throw new NotSupportedException();
    public Task StartAsync(CancellationToken token = default) => throw new NotSupportedException();
    public void BeginSelect(long nowTicks) => throw new NotSupportedException();
    public void CancelSelect() => throw new NotSupportedException();
    public void SetPendingSpawn(ICharacter character, IMapInstance instance, long sinceTicks) => throw new NotSupportedException();
    public PendingSpawn? TakePendingSpawn() => throw new NotSupportedException();
    public void SendTimeSyncPing() => throw new NotSupportedException();
    public void RequestInitialTimeSyncPing() => throw new NotSupportedException();
    public bool TakeInitialTimeSyncPingRequest() => throw new NotSupportedException();

    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp, long serverReceivedTicks) =>
        throw new NotSupportedException();

    public void UpdateSession() => throw new NotSupportedException();
    public void UpdateMap() { }
    public void FlushContinuations() => throw new NotSupportedException();
    public void FlushOutbox() => throw new NotSupportedException();
    public void EnqueueContinuation<T>(Task<T> task, Action<T> callback) => throw new NotSupportedException();
    public void EnqueueContinuation(Task task, Action callback) => throw new NotSupportedException();
}

