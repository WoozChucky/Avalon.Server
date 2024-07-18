using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public;

public interface IWorldConnection : IConnection
{
    public AccountId? AccountId { get; set; }
    public ICharacter? Character { get; set; }
    
    
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
    ISet<CreatureId> NewCreatures { get; }
    ISet<(CreatureId creatureId, GameEntityFields fields)> UpdatedCreatures { get; }
    ISet<CreatureId> RemovedCreatures { get; }
    ISet<CharacterId> NewCharacters { get; }
    ISet<(CharacterId creatureId, GameEntityFields fields)> UpdatedCharacters { get; }
    ISet<CharacterId> RemovedCharacters { get; }

    void Update(Dictionary<CreatureId, ICreature> creatures, Dictionary<CharacterId, ICharacter> characters);
}

public abstract class PacketFilter
{
    protected PacketFilter(IWorldConnection connection) { }
    
    public abstract bool Process(NetworkPacket packet);
    
    public abstract bool CanProcess(NetworkPacketType type);
}
