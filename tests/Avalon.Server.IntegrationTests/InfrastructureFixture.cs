using Avalon.Common.Cryptography;
using Avalon.Game;
using Avalon.Game.Configuration;
using Avalon.Infrastructure;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets.Internal;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Server.IntegrationTests;

public class InfrastructureFixture : IDisposable
{
	public CancellationTokenSource CancellationTokenSource { get; set; }
	
	public IAvalonInfrastructure Infrastructure { get; set; }
	public IReplicatedCache Cache { get; set; }
	public IAvalonNetworkDaemon NetworkDaemon { get; set; }
	public IMetricsManager MetricsManager { get; set; }
	
	public IAvalonTcpServer TcpServer { get; set; }
	public IAvalonSessionManager SessionManager { get; set; }
	
	public IPacketSerializer PacketSerializer { get; set; }
	public IPacketDeserializer PacketDeserializer { get; set; }
	public IPacketRegistry PacketRegistry { get; set; }
	public ICryptoManager CryptoManager { get; set; }
	
	public IAvalonGame Game { get; set; }
	public GameConfiguration GameConfiguration { get; set; } = new();
    
    public void Dispose()
    {
	    
    }
}
