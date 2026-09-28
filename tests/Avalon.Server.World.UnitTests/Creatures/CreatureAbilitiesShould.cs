using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>#163: a creature holds its own clones of the abilities its script declares, and their cooldowns.</summary>
public class CreatureAbilitiesShould
{
    private static readonly AbilityId Bite = new(302);
    private static readonly AbilityId Claw = new(303);

    internal static AbilityCatalog Catalog(params AbilityTemplate[] templates) =>
        new(templates, NullLoggerFactory.Instance);

    private static AbilityTemplate Row(AbilityId id, uint cooldownMs = 8000)
    {
        AbilityTemplate row = AbilityTestData.Cone(id.Value, reach: 1.8f, arc: 90f);
        row.Cooldown = cooldownMs;
        row.AllowedClasses = [];
        return row;
    }

    [Fact]
    public void Give_each_creature_its_own_clones()
    {
        AbilityCatalog catalog = Catalog(Row(Bite), Row(Claw));
        var wolf = new Creature { Name = "Wolf" };
        var other = new Creature { Name = "Wolf" };

        wolf.Abilities.Load(catalog, new CreatureAbilityKit(Bite, Claw), NullLogger.Instance, wolf.Name);
        other.Abilities.Load(catalog, new CreatureAbilityKit(Bite, Claw), NullLogger.Instance, other.Name);

        Assert.Equal([Bite, Claw], wolf.Abilities.All.Select(a => a.AbilityId));
        Assert.Same(wolf.Abilities[Bite], wolf.Abilities.Basic);
        Assert.NotSame(wolf.Abilities[Bite], other.Abilities[Bite]);

        wolf.Abilities[Claw]!.CooldownTimer = 5f;
        Assert.Equal(0f, other.Abilities[Claw]!.CooldownTimer);
    }

    [Fact]
    public void Tick_cooldowns_down_and_gate_readiness_on_them()
    {
        var wolf = new Creature { Name = "Wolf" };
        wolf.Abilities.Load(Catalog(Row(Bite), Row(Claw)), new CreatureAbilityKit(Bite, Claw), NullLogger.Instance, "Wolf");
        IAbility claw = wolf.Abilities[Claw]!;
        claw.CooldownTimer = 1f;

        Assert.False(CreatureAbilities.IsReady(claw));
        wolf.Abilities.Update(TimeSpan.FromSeconds(0.6));
        Assert.False(CreatureAbilities.IsReady(claw));
        wolf.Abilities.Update(TimeSpan.FromSeconds(0.6));
        Assert.True(CreatureAbilities.IsReady(claw));

        claw.Casting = true;
        Assert.False(CreatureAbilities.IsReady(claw));
        Assert.True(wolf.Abilities.IsCasting);
    }

    [Fact]
    public void Log_a_missing_id_and_keep_the_rest()
    {
        var logs = new ListLogger();
        var wolf = new Creature { Name = "Wolf" };

        wolf.Abilities.Load(Catalog(Row(Bite)), new CreatureAbilityKit(Bite, Claw), logs, "Wolf");

        Assert.Equal([Bite], wolf.Abilities.All.Select(a => a.AbilityId));
        Assert.NotNull(wolf.Abilities.Basic);
        (LogLevel level, string message) = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("303", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fight_with_the_specials_when_the_basic_is_missing()
    {
        var wolf = new Creature { Name = "Wolf" };

        wolf.Abilities.Load(Catalog(Row(Claw)), new CreatureAbilityKit(Bite, Claw), NullLogger.Instance, "Wolf");

        Assert.Null(wolf.Abilities.Basic);
        Assert.Equal([Claw], wolf.Abilities.All.Select(a => a.AbilityId));
    }

    [Fact]
    public void Load_an_id_named_twice_once()
    {
        var wolf = new Creature { Name = "Wolf" };

        wolf.Abilities.Load(Catalog(Row(Bite)), new CreatureAbilityKit(Bite, Bite), NullLogger.Instance, "Wolf");

        Assert.Single(wolf.Abilities.All);
    }

    [Fact]
    public void Take_every_cooldown_off_on_reset()
    {
        var wolf = new Creature { Name = "Wolf" };
        wolf.Abilities.Load(Catalog(Row(Bite), Row(Claw)), new CreatureAbilityKit(Bite, Claw), NullLogger.Instance, "Wolf");
        foreach (IAbility ability in wolf.Abilities.All)
            ability.CooldownTimer = 3f;

        wolf.Abilities.ResetCooldowns();

        Assert.All(wolf.Abilities.All, a => Assert.True(CreatureAbilities.IsReady(a)));
    }

    /// <summary>Every entry logged, with its level and formatted message.</summary>
    internal sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
