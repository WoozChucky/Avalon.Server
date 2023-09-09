using Avalon.Database;
using Avalon.Game;
using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Avalon.Game.Pools;
using Avalon.Game.Quests;
using Avalon.Game.Scripts;
using Avalon.Infrastructure;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets.Internal;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.IntegrationTests;

public class InfrastructureFixture : IDisposable
{
	public InfrastructureFixture()
	{
		CancellationTokenSource = new CancellationTokenSource();
		
		MetricsManager = Substitute.For<IMetricsManager>();
		TcpServer = Substitute.For<IAvalonTcpServer>();
		UdpServer = Substitute.For<IAvalonUdpServer>();
		PacketSerializer = new NetworkPacketSerializer();
		PacketDeserializer = new NetworkPacketDeserializer();
		PacketRegistry = new PacketRegistry();
		
		DatabaseManager = Substitute.For<IDatabaseManager>();

		ConnectionManager = new AvalonConnectionManager(
			new NullLogger<AvalonConnectionManager>()
		);

		CreatureSpawner = new CreatureSpawner(
			new NullLogger<CreatureSpawner>(),
			DatabaseManager
		);

		AIController = new AIController(new NullLogger<AIController>());

		PoolManager = new PoolManager(
			new NullLogger<PoolManager>(),
			CreatureSpawner,
			AIController
		);

		MapManager = new AvalonMapManager(
			Substitute.For<LoggerFactory>(),
			DatabaseManager,
			PoolManager
		);
		
		QuestManager = new QuestManager(
			new NullLogger<QuestManager>(),
			DatabaseManager
		);

		Game = new AvalonGame(
			new NullLogger<AvalonGame>(),
			PacketSerializer,
			ConnectionManager,
			DatabaseManager,
			MapManager,
			CreatureSpawner,
			AIController,
			PoolManager,
			QuestManager
		);

		NetworkDaemon = new AvalonNetworkDaemon(
			new NullLogger<AvalonNetworkDaemon>(),
			CancellationTokenSource,
			TcpServer,
			UdpServer,
			PacketDeserializer,
			PacketSerializer,
			PacketRegistry,
			Game,
			ConnectionManager,
			MetricsManager
		);
		
		Infrastructure = new AvalonInfrastructure(
			CancellationTokenSource, 
			new NullLogger<AvalonInfrastructure>(), 
			NetworkDaemon, 
			Game, 
			MetricsManager
		);
	}
	
	public CancellationTokenSource CancellationTokenSource { get; set; }
	
	public IAvalonInfrastructure Infrastructure { get; set; }
	public IAvalonNetworkDaemon NetworkDaemon { get; set; }
	public IMetricsManager MetricsManager { get; set; }
	
	public IAvalonTcpServer TcpServer { get; set; }
	public IAvalonUdpServer UdpServer { get; set; }
	public IAvalonConnectionManager ConnectionManager { get; set; }
	
	public IPacketSerializer PacketSerializer { get; set; }
	public IPacketDeserializer PacketDeserializer { get; set; }
	public IPacketRegistry PacketRegistry { get; set; }
	
	public IDatabaseManager DatabaseManager { get; set; }
	public IPoolManager PoolManager { get; set; }
	public IAvalonMapManager MapManager { get; set; }
	public IQuestManager QuestManager { get; set; }
	
	public ICreatureSpawner CreatureSpawner { get; set; }
	public IAIController AIController { get; set; }
	
	public IAvalonGame Game { get; set; }
    
    public void Dispose()
    {
	    
    }
}
