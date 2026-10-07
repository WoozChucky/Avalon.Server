using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>#163: a creature holds its own clones of the abilities its script declares, and their cooldowns.</summary>
public class CreatureAbilitiesShould
{
    private static readonly AbilityId s_bite = new(302);
    private static readonly AbilityId s_claw = new(303);

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
        AbilityCatalog catalog = Catalog(Row(s_bite), Row(s_claw));
        var wolf = new Creature { Name = "Wolf" };
        var other = new Creature { Name = "Wolf" };

        wolf.Abilities.Load(catalog, new CreatureAbilityKit(s_bite, s_claw), NullLogger.Instance, wolf.Name);
        other.Abilities.Load(catalog, new CreatureAbilityKit(s_bite, s_claw), NullLogger.Instance, other.Name);

        Assert.Equal([s_bite, s_claw], wolf.Abilities.All.Select(a => a.AbilityId));
        Assert.Same(wolf.Abilities[s_bite], wolf.Abilities.Basic);
        Assert.NotSame(wolf.Abilities[s_bite], other.Abilities[s_bite]);

        wolf.Abilities[s_claw]!.CooldownTimer = 5f;
        Assert.Equal(0f, other.Abilities[s_claw]!.CooldownTimer);
    }

    [Fact]
    public void Tick_cooldowns_down_and_gate_readiness_on_them()
    {
        var wolf = new Creature { Name = "Wolf" };
        wolf.Abilities.Load(Catalog(Row(s_bite), Row(s_claw)), new CreatureAbilityKit(s_bite, s_claw), NullLogger.Instance, "Wolf");
        IAbility claw = wolf.Abilities[s_claw]!;
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

    /// <summary>
    /// The kit's ids the catalog holds, each once: an id the catalog lacks (missing, or refused by it) is logged at
    /// Error and left out, and the creature fights with the rest, with its basic or without it.
    /// </summary>
    [Theory]
    [InlineData(new uint[] { 302 }, new uint[] { 302, 303 }, new uint[] { 302 }, "303")]   // a special missing
    [InlineData(new uint[] { 303 }, new uint[] { 302, 303 }, new uint[] { 303 }, "302")]   // the basic missing
    [InlineData(new uint[] { 302 }, new uint[] { 302, 302 }, new uint[] { 302 }, null)]    // an id named twice
    public void Load_each_kit_id_the_catalog_holds_once_and_log_the_rest(uint[] catalog, uint[] kit, uint[] loaded,
        string? missing)
    {
        var logs = new ListLogger();
        var wolf = new Creature { Name = "Wolf" };

        wolf.Abilities.Load(Catalog([.. catalog.Select(id => Row(new AbilityId(id)))]),
            new CreatureAbilityKit(new AbilityId(kit[0]), [.. kit.Skip(1).Select(id => new AbilityId(id))]), logs, "Wolf");

        Assert.Equal(loaded, wolf.Abilities.All.Select(a => a.AbilityId.Value));
        Assert.Equal(loaded.Contains(kit[0]), wolf.Abilities.Basic is not null);
        if (missing is null)
        {
            Assert.Empty(logs.Entries);
            return;
        }

        (LogLevel level, string message) = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains(missing, message, StringComparison.Ordinal);
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
