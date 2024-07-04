namespace Avalon.Hosting.Networking;

public interface IAuthConnection : IConnection
{
    public int? AccountId { get; set; }
    
    byte[] GenerateHandshakeData();
    bool VerifyHandshakeData(byte[] handshakeData);
}
