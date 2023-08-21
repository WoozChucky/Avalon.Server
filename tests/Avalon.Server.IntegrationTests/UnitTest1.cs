namespace Avalon.Server.IntegrationTests;

public class UnitTest1 : IClassFixture<InfrastructureFixture>
{
    private readonly InfrastructureFixture _fixture;
    
    public UnitTest1(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Test1()
    {
        _fixture.Infrastructure.Start();
    }
}
