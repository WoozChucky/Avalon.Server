namespace Avalon.Hosting.Network;

public interface IWorldConnection : IConnection
{
    public uint? AccountId { get; set; }
    public uint? CharacterId { get; set; }
}