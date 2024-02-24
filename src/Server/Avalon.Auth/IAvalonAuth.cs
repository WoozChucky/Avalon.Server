using Avalon.Network;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Handshake;

namespace Avalon.Auth;

public interface IAvalonAuth
{
    void Start();
    void Stop();
    void Update(TimeSpan deltaTime);
    
    long GetLoopCounter();
    void IncrementLoopCounter();
    bool IsRunning();
    Task HandleServerInfoPacket(IRemoteSource source, CRequestServerInfoPacket packet);
    Task HandleClientInfoPacket(IRemoteSource source, CClientInfoPacket packet);
    Task HandleHandshakePacket(IRemoteSource source, CHandshakePacket packet);
    Task HandleAuthPacket(IRemoteSource source, CAuthPacket packet);
    Task HandleLogoutPacket(IRemoteSource source, CLogoutPacket packet);
    Task HandleRegisterPacket(IRemoteSource source, CRegisterPacket packet);
}
