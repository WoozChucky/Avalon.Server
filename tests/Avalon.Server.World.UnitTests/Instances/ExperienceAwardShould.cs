using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

public class ExperienceAwardShould
{
    /// <summary>
    /// BandScale being correct proves nothing on its own — it is a static helper, and deleting its call
    /// from <c>OnCreatureKilled</c> leaves every other test in the suite green. This drives a real kill
    /// through a real <c>MapInstance</c> on a banded map and asserts the award the character actually
    /// receives, which is the only thing that pins the wiring.
    /// </summary>
    [Fact]
    public void Scale_The_Award_A_Character_Actually_Receives_By_The_Maps_Band()
    {
        // ForestDungeon's band is 1-5; a level 9 character is four levels out, so 0.75^4 = 0.3164.
        const int creatureExperience = 1000;
        var world = Substitute.For<Avalon.World.IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(1), Name = "ForestDungeon", MinLevel = 1, MaxLevel = 5 }
        });
        // Built into a local first: calling it inside Returns(...) would make the last substitute
        // call inside LoadedStaticData the one Returns binds to, which NSubstitute rejects.
        StaticData data = LoadedStaticData();
        world.Data.Returns(data);

        MapInstance instance = BuildInstance(world);

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 880_001),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Experience = creatureExperience
        };
        instance.AddCreature(creature);

        ICharacter killer = Substitute.For<ICharacter>();
        killer.Guid.Returns(new ObjectGuid(ObjectType.Character, 880_002));
        killer.Level.Returns((ushort)9);
        killer.Experience.Returns(0ul);

        creature.Died(killer);

        // 1000 * 0.75^4 = 316.4 -> 316. A wiring that skipped the band would award the full 1000.
        killer.Received().Experience = 316;
        instance.Dispose();
    }

    [Theory]
    [InlineData(3, 1, 5, 1.0)]      // inside the band
    [InlineData(1, 1, 5, 1.0)]      // on the lower edge
    [InlineData(5, 1, 5, 1.0)]      // on the upper edge
    [InlineData(6, 1, 5, 0.75)]     // one over
    [InlineData(10, 1, 5, 0.2373)]  // five over
    [InlineData(14, 1, 5, 0.0751)]  // nine over
    [InlineData(1, 2, 5, 0.75)]     // one under, symmetric
    public void Scale_Experience_By_Distance_Outside_The_Band(
        int playerLevel, int bandMin, int bandMax, double expected)
    {
        double actual = MapInstance.BandScale((ushort)playerLevel, (ushort)bandMin, (ushort)bandMax, 0.75f);

        Assert.Equal(expected, actual, precision: 3);
    }

    /// <summary>
    /// <c>MapTemplate.MinLevel</c> and <c>MaxLevel</c> are both nullable. A map with no band scales
    /// nothing — it must not be read as a band of 0-0, which would wipe out every award on that map.
    /// </summary>
    [Fact]
    public void Not_Scale_At_All_When_The_Map_Has_No_Band()
    {
        Assert.Equal(1.0, MapInstance.BandScale(40, null, null, 0.75f), precision: 3);
        Assert.Equal(1.0, MapInstance.BandScale(40, 1, null, 0.75f), precision: 3);
        Assert.Equal(1.0, MapInstance.BandScale(40, null, 5, 0.75f), precision: 3);
    }

    [Fact]
    public void Never_Return_A_Negative_Scale_However_Far_Out_The_Player_Is()
    {
        double scale = MapInstance.BandScale(60000, 1, 5, 0.75f);

        Assert.True(scale >= 0.0, $"scale went negative at {scale}");
        Assert.True(scale < 0.001, "an absurd level difference should award essentially nothing");
    }

    private static MapInstance BuildInstance(Avalon.World.IWorld world)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(
            Seed: 0,
            Chunks: [entryChunk],
            EntryChunk: entryChunk,
            BossChunk: null,
            Portals: [],
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: null);

        return new MapInstance(
            NullLoggerFactory.Instance,
            serviceProvider,
            world,
            new MapTemplateId(1),
            ownerCharacterId: null,
            layout,
            Substitute.For<IMapNavigator>(),
            seed: 0);
    }

    /// <summary>
    /// StaticData's collections are null until LoadAsync runs, and OnCreatureKilled reads
    /// CharacterLevelExperiences to decide whether the kill levelled the character up.
    /// </summary>
    private static StaticData LoadedStaticData()
    {
        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());

        var stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());

        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());

        var abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AbilityTemplate>());

        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        // Well above anything a single scaled award can reach, so this kill cannot level the
        // character up and the assertion is on the award itself.
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>(
                [new CharacterLevelExperience { Level = 9, Experience = 6500 }]));

        var creatureTemplates = Substitute.For<ICreatureTemplateRepository>();
        creatureTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate>()));

        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(
                [new CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));

        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>([]));

        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var data = new StaticData(createInfos, stats, items, abilities, levels,
            creatureTemplates, baseStats, rarities,
            localizedText, dialogue, NullLoggerFactory.Instance);
        data.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return data;
    }
}
