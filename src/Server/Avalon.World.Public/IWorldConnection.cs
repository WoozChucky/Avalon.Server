using Avalon.Common.Threading;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public.Characters;

namespace Avalon.World.Public;

public interface IWorldConnection : IConnection
{
    public AccountId? AccountId { get; set; }
    public ICharacter? Character { get; set; }
    
    public IGameState GameState { get; }
    
    
    public long Latency { get; }
    public long RoundTripTime { get; }
    
    public bool InGame { get; }
    public bool InMap { get; }
    void EnableTimeSyncWorker();
    void OnPongReceived(long packetLastServerTimestamp, long packetClientReceivedTimestamp, long packetClientSentTimestamp);
    
    void Update(TimeSpan deltaTime, PacketFilter filter);
    
    
}

public interface IGameState
{
    public ISet<Guid> KnownEntities { get; }
    public ISet<CharacterId> KnownCharacters { get; }
    
    public ISet<object> KnownObjects { get; }
}

public abstract class PacketFilter
{
    protected PacketFilter(IWorldConnection connection) { }
    
    public abstract bool Process(NetworkPacket packet);
    
    public abstract bool CanProcess(NetworkPacketType type);
}
