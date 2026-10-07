using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.World;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// Aura time is paused while a character moves between instances: held from its removal, resumed when the next instance
/// adds it, so the time between costs no tick and pays none in a burst.
/// </summary>
public class AuraTransferShould
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan s_frame = TimeSpan.FromSeconds(1d / 60d);
    private readonly FakeTimeProvider _clock = new(s_t0);

    private CharacterEntity Character(uint id)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = $"Tester{id}",
            Class = CharacterClass.Warrior,
            CreationDate = DateTime.UtcNow,
        };
        return new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), _clock)
        {
            Data = row,
            Health = 500,
            CurrentHealth = 400,
        };
    }

    private static async Task<StaticData> DataAsync() =>
        await TestStaticData.LoadAsync(TestStaticData.Repositories(
            classStats: () => [new ClassLevelStat { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 }],
            auras: () => [AuraTestData.Bleed()]));

    /// <summary>A Bleed of one stack with 7.5 s and three ticks of 3 left, restored at select.</summary>
    private static void GiveBleed(CharacterEntity character, StaticData data) =>
        AuraRestore.Restore(character,
        [
            new CharacterAura
            {
                CharacterId = new CharacterId(914_000), Slot = 0, AuraId = 901, CasterGuid = 0, Stacks = 1, RemainingMs = 7500,
                DurationMs = 12000, TicksLeft = 3, TickAmount = 3f, CritPct = 0f, CasterLevel = 1, AppliedAt = s_t0.UtcDateTime,
            },
        ], data, 32, NullLogger.Instance);

    private MapInstance Instance(StaticData data) => TestMapInstances.BuildCasting(out _, world: NewWorld(data), time: _clock);

    /// <summary>The removal and the add a transfer makes, with <paramref name="gap" /> between them.</summary>
    private void Move(MapInstanceClient client, MapInstance from, MapInstance to, TimeSpan gap)
    {
        from.RemoveCharacter(client.Connection);
        client.Character.Auras.Hold(_clock.GetUtcNow());
        _clock.Advance(gap);
        client.Character.InstanceId = to.InstanceId;
        to.AddCharacter(client.Connection);
    }

    [Fact]
    public async Task Hold_the_auras_from_the_removal_until_the_target_adds_the_character()
    {
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            Substitute.For<IScriptHotReloader>(), intervalSeconds: 60);
        CharacterEntity character = Character(914_001);
        GiveBleed(character, await DataAsync());
        character.Auras.ResumeHeld(_clock.GetUtcNow());   // in the world
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        IMapInstance target = Substitute.For<IMapInstance>();
        DateTimeOffset? heldAtArrival = null;
        target.When(t => t.AddCharacter(connection)).Do(_ => heldAtArrival = character.Auras.HeldSince);

        world.TransferPlayer(connection, target);

        Assert.Equal(_clock.GetUtcNow(), heldAtArrival);
    }

    /// <summary>
    /// 10 s pass between the two instances: the aura's end moves by them, its two remaining ticks land 3 and 6 s after
    /// the arrival, each once, and nothing is paid for the gap.
    /// </summary>
    [Fact]
    public async Task Neither_lose_nor_burst_a_tick_across_a_gap_between_instances_and_move_the_end_by_the_gap()
    {
        StaticData data = await DataAsync();
        MapInstance from = Instance(data);
        MapInstance to = Instance(data);
        Join(to, Character(914_010));   // occupied, so it runs its aura pass
        CharacterEntity character = Character(914_011);
        GiveBleed(character, data);
        MapInstanceClient client = Join(from, character);
        _clock.Advance(TimeSpan.FromMilliseconds(1500));
        from.Update(s_frame);
        Assert.Equal(397u, character.CurrentHealth);
        DateTimeOffset end = Assert.Single(character.Auras.All).Schedule.ExpiresAt;

        Move(client, from, to, TimeSpan.FromSeconds(10));

        ActiveAura bleed = Assert.Single(character.Auras.All);
        Assert.Equal((end.AddSeconds(10), 2), (bleed.Schedule.ExpiresAt, bleed.Schedule.TicksLeft));
        Assert.Null(character.Auras.HeldSince);
        to.Update(s_frame);
        Assert.Equal(397u, character.CurrentHealth);   // nothing paid for the gap

        _clock.Advance(TimeSpan.FromSeconds(3));
        to.Update(s_frame);
        Assert.Equal(394u, character.CurrentHealth);
        _clock.Advance(TimeSpan.FromSeconds(3));
        to.Update(s_frame);
        Assert.Equal(391u, character.CurrentHealth);
        Assert.Equal(0, character.Auras.Count);
    }

    /// <summary>
    /// A tick came due just before the character left, and the instance it arrives in stood empty: the tick is still
    /// owed, and paid once on the first pass there, not skipped with the empty instance's own.
    /// </summary>
    [Fact]
    public async Task Keep_a_tick_owed_at_the_removal_on_arriving_in_an_instance_that_stood_empty()
    {
        StaticData data = await DataAsync();
        MapInstance from = Instance(data);
        MapInstance to = Instance(data);
        to.Update(s_frame);   // empty, it now stands still
        CharacterEntity character = Character(914_021);
        GiveBleed(character, data);
        MapInstanceClient client = Join(from, character);
        _clock.Advance(TimeSpan.FromMilliseconds(1500));   // the first tick is due; no pass took it

        Move(client, from, to, TimeSpan.FromSeconds(2));
        _clock.Advance(s_frame);
        to.Update(s_frame);

        Assert.Equal(397u, character.CurrentHealth);
        Assert.Equal(2, Assert.Single(character.Auras.All).Schedule.TicksLeft);
    }
}
