using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Characters;
using Avalon.Database.World;
using Avalon.Game;
using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Avalon.Game.Pools;
using Avalon.Game.Quests;
using Avalon.Game.Scripts;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
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
		
		AuthDatabase = Substitute.For<IAuthDatabase>();
		CharactersDatabase = Substitute.For<ICharactersDatabase>();
		WorldDatabase = Substitute.For<IWorldDatabase>();
		
		var mockedLoggerFactory = Substitute.For<LoggerFactory>();

		DatabaseManager = new DatabaseManager(
			AuthDatabase,
			CharactersDatabase,
			WorldDatabase
		);

		ConnectionManager = new AvalonConnectionManager(
			mockedLoggerFactory
		);

		CreatureSpawner = new CreatureSpawner(
			mockedLoggerFactory,
			DatabaseManager
		);

		AIController = new AIController(
			mockedLoggerFactory
		);

		PoolManager = new PoolManager(
			mockedLoggerFactory,
			CreatureSpawner,
			AIController
		);

		MapManager = new AvalonMapManager(
			mockedLoggerFactory,
			DatabaseManager,
			PoolManager
		);
		
		QuestManager = new QuestManager(
			mockedLoggerFactory,
			DatabaseManager
		);
		
		CryptoManager = new CryptoManager();

		Game = new AvalonGame(
			mockedLoggerFactory,
			ConnectionManager,
			DatabaseManager,
			MapManager,
			CreatureSpawner,
			AIController,
			PoolManager,
			QuestManager,
			CryptoManager
		);

		NetworkDaemon = new AvalonNetworkDaemon(
			mockedLoggerFactory,
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
			mockedLoggerFactory,
			CancellationTokenSource,
			new InfrastructureConfiguration
			{
				MinUpdateDiff = 1,
				MaxCoreStuckTime = 60000
			},
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
	public IAuthDatabase AuthDatabase { get; set; }
	public IWorldDatabase WorldDatabase { get; set; }
	public ICharactersDatabase CharactersDatabase { get; set; }
	public IPoolManager PoolManager { get; set; }
	public IAvalonMapManager MapManager { get; set; }
	public IQuestManager QuestManager { get; set; }
	
	public ICreatureSpawner CreatureSpawner { get; set; }
	public IAIController AIController { get; set; }
	public ICryptoManager CryptoManager { get; set; }
	
	public IAvalonGame Game { get; set; }
    
    public void Dispose()
    {
	    
    }
}
