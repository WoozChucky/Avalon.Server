using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts;

public class TestAiScript : AiScript
{
    private readonly ILogger<TestAiScript> _logger;
    
    public TestAiScript(ILoggerFactory loggerFactory, ICreature creature, IChunk chunk) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<TestAiScript>();
        _logger.LogInformation("TestAiScript instanciated");
    }

    public override object State { get; set; }
    protected override bool ShouldRun()
    {
        return false;
    }
}
