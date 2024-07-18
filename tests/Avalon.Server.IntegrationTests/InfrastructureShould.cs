using NSubstitute;

namespace Avalon.Server.IntegrationTests;

public class InfrastructureShould : IClassFixture<InfrastructureFixture>
{
    private readonly InfrastructureFixture _fixture;
    
    public InfrastructureShould(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void OnStart_InitializeAllSystems_And_GetAllTemplatesFromDatabase()
    {
        // Prepare
        
        // Act
        //_fixture.Infrastructure.Start();
        
        // Assert
        //_fixture.MetricsManager.Received(1).QueueEvent("AvalonInfrastructureStatus", "Online");
        //_fixture.TcpServer.Received(1).RunAsync();
        // _fixture.UdpServer.Received(1).RunAsync();

        /*
        _fixture.DatabaseManager.World.Received(1).Map.FindAllAsync();
        _fixture.DatabaseManager.World.Received(1).CreatureTemplate.FindAllAsync();
        _fixture.DatabaseManager.World.Received(1).QuestReward.FindAllAsync();
        _fixture.DatabaseManager.World.Received(1).QuestTemplate.FindAllAsync();
        _fixture.DatabaseManager.World.Received(1).QuestRewardTemplate.FindAllAsync();
        */
    }
}
