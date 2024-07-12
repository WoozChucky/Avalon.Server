using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.World.Public.Characters;

namespace Avalon.World.Public;

public interface IWorldConnection : IConnection
{
    public AccountId? AccountId { get; set; }
    public CharacterId? CharacterId { get; set; }
    public ICharacter? Character { get; set; }
    
    public long Latency { get; }
    public long RoundTripTime { get; }
    
    public bool InGame { get; }
    public bool InMap { get; }
    void EnableTimeSyncWorker();
    void OnPongReceived(long packetLastServerTimestamp, long packetClientReceivedTimestamp, long packetClientSentTimestamp);
}
