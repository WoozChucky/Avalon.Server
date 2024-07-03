namespace Avalon.Hosting.Network;

public interface IAuthConnection : IConnection
{
    public uint? AccountId { get; set; }
}