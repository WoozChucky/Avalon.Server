using Avalon.World;
using Avalon.World.Quests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// #738: the provider quest scripts are built from hands over a logger factory, loggers and the clock, from the
/// container, and nothing else, whatever the container holds: so a script cannot obtain a service that writes.
/// </summary>
public class QuestScriptServicesShould
{
    private static readonly ManualTimerClock s_clock = new();

    private static IServiceProvider Container() => new ServiceCollection()
        .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
        .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
        .AddSingleton<TimeProvider>(s_clock)
        .AddSingleton(Substitute.For<IWorld>())
        .AddSingleton(new object())
        .BuildServiceProvider();

    [Theory]
    [InlineData(typeof(IWorld))]
    [InlineData(typeof(object))]
    [InlineData(typeof(IServiceProvider))]
    [InlineData(typeof(IServiceScopeFactory))]
    [InlineData(typeof(IEnumerable<ILoggerFactory>))]
    public void Hand_over_nothing_else(Type type)
    {
        IServiceProvider container = Container();
        Assert.NotNull(container.GetService(type));

        Assert.Null(new QuestScriptServices(container).GetService(type));
    }
}
