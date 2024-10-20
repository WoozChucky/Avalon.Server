using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;

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

    public double LastMovementTime { get; }

}

public interface IGameState
{
    ISet<ObjectGuid> NewObjects { get; }
    ISet<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects { get; }
    ISet<ObjectGuid> RemovedObjects { get; }

    void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> chunkObjects);
}

public abstract class PacketFilter
{
    protected PacketFilter(IWorldConnection connection) { }

    public abstract bool Process(NetworkPacket packet);

    public abstract bool CanProcess(NetworkPacketType type);
}
