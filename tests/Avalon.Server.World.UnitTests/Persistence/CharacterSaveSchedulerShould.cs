using Avalon.Common.ValueObjects;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Persistence;

public class CharacterSaveSchedulerShould
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    [Fact]
    public void Default_to_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new GameConfiguration().CharacterSaveInterval);
    }

    [Fact]
    public void Read_the_interval_from_configuration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Game:CharacterSaveInterval"] = "00:02:30" })
            .Build();

        GameConfiguration bound = new();
        configuration.GetSection("Game").Bind(bound);

        Assert.Equal(TimeSpan.FromSeconds(150), bound.CharacterSaveInterval);
    }

    [Fact]
    public void Save_when_the_first_delay_runs_out_and_then_every_interval()
    {
        var saver = Substitute.For<ICharacterSaver>();
        var scheduler = new CharacterSaveScheduler(saver, Options.Create(new GameConfiguration { CharacterSaveInterval = Interval }));
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        CharacterEntity character = New(7);
        TimeSpan first = CharacterSaveScheduler.FirstSaveDelay(new CharacterId(7), Interval);

        scheduler.Tick(connection, character, first - Tick);
        saver.DidNotReceiveWithAnyArgs().Save(default(IWorldConnection)!, default!);

        scheduler.Tick(connection, character, Tick);
        saver.Received(1).Save(connection, character);

        scheduler.Tick(connection, character, Interval - Tick);
        saver.Received(1).Save(connection, character);

        scheduler.Tick(connection, character, Tick);
        saver.Received(2).Save(connection, character);
    }

    [Fact]
    public void Spread_first_saves_across_one_interval()
    {
        TimeSpan interval = TimeSpan.FromMinutes(5);

        List<TimeSpan> delays = Enumerable.Range(1, 100)
            .Select(id => CharacterSaveScheduler.FirstSaveDelay(new CharacterId((uint)id), interval))
            .ToList();

        Assert.All(delays, delay => Assert.InRange(delay, TimeSpan.Zero, interval - TimeSpan.FromTicks(1)));
        // A hundred characters, a hundred different ticks.
        Assert.Equal(100, delays.Select(delay => (long)(delay / Tick)).Distinct().Count());
    }
}
