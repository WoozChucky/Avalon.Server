using System.Reflection;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Talents;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Talents;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Reload;
using Avalon.World.Talents;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_TALENT_ALLOCATE (#1181) through the real handler, over the seeded tree, for a level-10 Warrior in a real
/// instance holding the spoke's first five nodes (57-61, Hurled Axe at 61), so 5 of its 10 points are left, with Hurled
/// Axe cooling down: every request gets one SMSG_TALENT_RESULT; an accepted one holds its nodes, marks each for the
/// save, refreshes the stats and owes the talent state, and the ability list only when the abilities held changed (a
/// held ability keeps its running cooldown, a new one arrives ready); the result is the request's only packet, and the
/// tick's TalentFlusher sends what is owed; a refusal changes nothing and owes nothing. A stats refresh that throws
/// after the abilities were rebuilt answers InternalError, and the new ability list and the state still follow. A
/// starter the ability catalog does not hold is left out of the rebuilt list, and the held nodes' abilities are not.
/// Cleave refunded after an ability reload that changed it comes back built from the reloaded catalog, never the
/// ability it was before (Part C). Character id 118_101 is this class's.
/// </summary>
public class TalentAllocateHandlerShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly AbilityId s_heavyStrike = new(319), s_cleave = new(200), s_hurledAxe = new(202);
    private static readonly int[] s_spoke = [57, 58, 59, 60, 61];

    private const uint Me = 118_101, RequestId = 23, ReloadedCleaveCost = 17;

    [Theory]
    [InlineData("small nodes")]
    [InlineData("ability node")]
    [InlineData("refused")]
    [InlineData("refresh throws")]
    [InlineData("starter missing")]
    [InlineData("taken again after a reload")]
    [InlineData("no character")]
    public void Apply_an_accepted_allocation_and_change_nothing_on_a_refusal(string row)
    {
        if (row == "no character")
        {
            IWorldConnection nobody = Substitute.For<IWorldConnection>();
            nobody.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null);
            new TalentAllocateHandler(NullLogger<TalentAllocateHandler>.Instance, new QuietWorld(s_data))
                .Execute(nobody, Request(2));
            nobody.DidNotReceive().Send(Arg.Any<OutboundPacket>());
            return;
        }

        var world = new QuietWorld(s_data);
        using MapInstance instance = TestMapInstances.Build(world);
        CharacterEntity character = TestCharacters.New(Me);
        character.Level = 10;
        MapInstanceClient client = Join(instance, character);
        TalentTestCharacters.Enter(character, s_data, s_spoke);
        IAbility hurledAxe = character.Spells[s_hurledAxe]!;
        hurledAxe.CooldownTimer = 2f;
        // Cleave taken under the seeded catalog, then refunded under a reload that made it cost more.
        StaticData reloaded = Areas(stats: true, cleaveCost: ReloadedCleaveCost);
        IAbility? cleaveBefore = null;
        if (row == "taken again after a reload")
        {
            character.Talents.Allocate([2, 3, 4, 5, 6]);
            TalentChange.Apply(character, s_data, NullLogger.Instance);
            cleaveBefore = character.Spells[s_cleave]!;
            foreach (int node in (int[])[2, 3, 4, 5, 6])
                character.Talents.Drop(node);
            TalentChange.Apply(character, reloaded, NullLogger.Instance);
            TalentFlusher.Flush(client.Connection, s_data.Quests, new TalentConfiguration());
            character.SaveState.Acknowledge(character.SaveState.TakeMarks());
        }

        IAbility[] abilitiesBefore = [.. character.Spells.All];
        DerivedCharacterStats statsBefore = character.Stats!.Value;
        client.Sent.Clear();
        var handler = new TalentAllocateHandler(NullLogger<TalentAllocateHandler>.Instance, row switch
        {
            "refresh throws" => new QuietWorld(Areas(stats: false)),
            "starter missing" => new QuietWorld(Areas(stats: true, without: s_heavyStrike)),
            "taken again after a reload" => new QuietWorld(reloaded),
            _ => world,
        });

        uint[] request = row switch
        {
            "small nodes" => [2, 3],
            "refused" => [2, 3, 2],
            _ => [2, 3, 4, 5, 6],
        };
        handler.Execute(client.Connection, Request(request));

        // The result is the only packet the request sends: the ability list follows at the tick's flush.
        STalentResultPacket result =
            Assert.Single(client.Read<STalentResultPacket>(NetworkPacketType.SMSG_TALENT_RESULT));
        Assert.Single(client.Sent);
        client.Sent.Clear();
        TalentFlusher.Flush(client.Connection, s_data.Quests, new TalentConfiguration());
        List<SCharacterAbilitiesPacket> abilityLists =
            client.Read<SCharacterAbilitiesPacket>(NetworkPacketType.SMSG_CHARACTER_ABILITIES);
        SaveMarks saved = character.SaveState.TakeMarks();
        IReadOnlyDictionary<int, SaveMark>? marks = saved.Talents;
        Assert.Equal(RequestId, result.RequestId);

        if (row == "refused")
        {
            Assert.Equal((TalentResult.AlreadyAllocated, 2u), (result.Result, result.NodeId));
            Assert.Equal(s_spoke, character.Talents.Held);
            Assert.Empty(marks!);
            Assert.Null(saved.StatsVersion);
            Assert.Equal(statsBefore.Strength, character.Stats!.Value.Strength);
            Assert.Equal(abilitiesBefore, character.Spells.All);
            Assert.Empty(client.Sent);
            return;
        }

        Assert.Equal((row == "refresh throws" ? TalentResult.InternalError : TalentResult.Ok, 0u),
            (result.Result, result.NodeId));
        Assert.Equal([.. request.Select(n => (int)n).Order()], marks!.Keys.Order());
        Assert.Equal([.. request.Select(n => (int)n).Concat(s_spoke).Order()], character.Talents.Held);
        Assert.Single(client.Read<STalentStatePacket>(NetworkPacketType.SMSG_TALENT_STATE));

        if (row == "small nodes")
        {
            DerivedCharacterStats stats = character.Stats!.Value;
            Assert.Equal(statsBefore.Strength + 5, stats.Strength);
            Assert.Equal(statsBefore.Offence.Increased(OffenceStat.Physical) + 5,
                stats.Offence.Increased(OffenceStat.Physical));
            Assert.Empty(abilityLists);
            Assert.Equal(abilitiesBefore, character.Spells.All);
            return;
        }

        // The starter, then each held ability node's by ascending node id: Cleave at 6, Hurled Axe at 61.
        SCharacterAbilitiesPacket sent = Assert.Single(abilityLists);
        AbilityId[] expected =
            row == "starter missing" ? [s_cleave, s_hurledAxe] : [s_heavyStrike, s_cleave, s_hurledAxe];
        Assert.Equal(expected.Select(a => a.Value), sent.Abilities.Select(a => a.AbilityId));
        Assert.Equal(expected, character.Spells.All.Select(a => a.AbilityId));
        Assert.Same(hurledAxe, character.Spells[s_hurledAxe]);
        Assert.Equal(2f, hurledAxe.CooldownTimer);
        Assert.Equal(0f, character.Spells[s_cleave]!.CooldownTimer);
        if (row == "taken again after a reload")
        {
            Assert.NotSame(cleaveBefore, character.Spells[s_cleave]);
            Assert.Equal(ReloadedCleaveCost, character.Spells[s_cleave]!.Metadata.Cost);
            Assert.Equal(ReloadedCleaveCost, sent.Abilities.Single(a => a.AbilityId == s_cleave.Value).Cost);
        }
    }

    private static CTalentAllocatePacket Request(params uint[] nodeIds) =>
        new() { RequestId = RequestId, NodeIds = [.. nodeIds] };

    /// <summary>
    /// The seeded tree and abilities, <paramref name="without" /> one of them when given, Cleave costing
    /// <paramref name="cleaveCost" /> when given (a reload that changed it), and with <paramref name="stats" /> the
    /// seeded items, combat and progression areas the stats refresh reads; without them the refresh throws.
    /// </summary>
    private static StaticData Areas(bool stats, AbilityId? without = null, uint? cleaveCost = null)
    {
        var data = new StaticData(Substitute.For<ICharacterCreateInfoRepository>(),
            Substitute.For<IClassLevelStatRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<IAbilityTemplateRepository>(),
            Substitute.For<ICharacterLevelExperienceRepository>(), Substitute.For<ICreatureTemplateRepository>(),
            Substitute.For<ICreatureBaseStatRepository>(), Substitute.For<ICreatureRarityModifierRepository>(),
            Substitute.For<ILocalizedTextRepository>(), Substitute.For<IDialogueRepository>(),
            Substitute.For<ILootTableRepository>(), NullLoggerFactory.Instance);
        AbilityCatalog abilities = (without, cleaveCost) switch
        {
            ({ } missing, _) => new AbilityCatalog([.. s_data.Abilities.Templates.Where(t => t.Id != missing)],
                NullLoggerFactory.Instance),
            (_, { } cost) => new AbilityCatalog(
                [.. s_data.Abilities.Templates.Select(t => t.Id == s_cleave ? Costing(t, cost) : t)],
                NullLoggerFactory.Instance),
            _ => s_data.Abilities,
        };
        data.Apply(new AbilitiesPatch(abilities, s_data.Talents));
        if (stats)
        {
            data.Apply(s_data.Items);
            data.Apply(s_data.Combat);
            data.Apply(new ProgressionPatch(s_data.CharacterLevelExperiences, s_data.ClassLevelStats,
                s_data.CharacterCreateInfos));
        }

        return data;
    }

    /// <summary>
    /// A copy of <paramref name="template" /> costing <paramref name="cost" />: the seeded row is shared, so it is not
    /// changed.
    /// </summary>
    private static AbilityTemplate Costing(AbilityTemplate template, uint cost)
    {
        var copy = new AbilityTemplate();
        foreach (PropertyInfo property in typeof(AbilityTemplate).GetProperties().Where(p => p.CanWrite))
            property.SetValue(copy, property.GetValue(template));
        copy.Cost = cost;
        return copy;
    }
}
