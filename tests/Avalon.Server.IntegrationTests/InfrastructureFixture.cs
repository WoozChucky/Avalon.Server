using Avalon.Common.Cryptography;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Characters;
using Avalon.Database.World;
using Avalon.Game;
using Avalon.Game.Configuration;
using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Avalon.Game.Pools;
using Avalon.Game.Quests;
using Avalon.Game.Scripts;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
using Avalon.Infrastructure.World;
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
		PacketSerializer = new NetworkPacketSerializer();
		PacketDeserializer = new NetworkPacketDeserializer();
		PacketRegistry = new PacketRegistry();
		
		AuthDatabase = Substitute.For<IAuthDatabase>();
		CharactersDatabase = Substitute.For<ICharactersDatabase>();
		WorldDatabase = Substitute.For<IWorldDatabase>();
		
		var mockedLoggerFactory = Substitute.For<LoggerFactory>();
		
		var mockedCache = Substitute.For<IReplicatedCache>();

		DatabaseManager = new DatabaseManager(
			AuthDatabase,
			CharactersDatabase,
			WorldDatabase
		);

		SessionManager = new AvalonSessionManager(
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
			PoolManager,
			SessionManager,
			GameConfiguration
		);
		
		QuestManager = new QuestManager(
			mockedLoggerFactory,
			DatabaseManager
		);
		
		CryptoManager = new CryptoManager();

		Game = new AvalonGame(
			mockedLoggerFactory,
			SessionManager,
			DatabaseManager,
			MapManager,
			CreatureSpawner,
			AIController,
			PoolManager,
			QuestManager,
			CryptoManager,
			GameConfiguration
		);

		NetworkDaemon = new AvalonWorldNetworkDaemon(
			mockedLoggerFactory,
			CancellationTokenSource,
			TcpServer,
			PacketDeserializer,
			PacketSerializer,
			PacketRegistry,
			Game,
			SessionManager,
			MetricsManager
		);
		
		Infrastructure = new AvalonWorldInfrastructure(
			mockedLoggerFactory,
			CancellationTokenSource,
			new InfrastructureConfiguration
			{
				MinUpdateDiff = 1,
				MaxCoreStuckTime = 60000
			},
			NetworkDaemon, 
			Game, 
			mockedCache,
			MetricsManager
		);
	}
	
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
	public GameConfiguration GameConfiguration { get; set; } = new();
    
    public void Dispose()
    {
	    
    }
}
