namespace Avalon.Hosting.Networking;

public interface IWorldConnection : IConnection
{
    public uint? AccountId { get; set; }
    public uint? CharacterId { get; set; }
}