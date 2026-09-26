using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Dialogue;

/// <summary>
/// Holds the flag a client reads to decide whether to offer an interact prompt to the rule the
/// server applies when the interact arrives. Both are computed from one real dialogue catalog, for
/// one real spawned creature, so a creature that advertises <c>CanInteract</c> is exactly one the
/// handler opens a conversation with, and a creature that does not is one it silently ignores.
/// </summary>
public class CanInteractAgreementShould
{
    private static readonly CreatureTemplateId Innkeeper = new(3);
    private static readonly CreatureTemplateId Wolf = new(4);

    [Theory]
    [InlineData(3ul, true)]
    [InlineData(4ul, false)]
    public void Advertise_Interaction_Exactly_When_The_Handler_Accepts_It(ulong templateId, bool hasDialogue)
    {
        IWorld world = WorldWithDialogueFor(Innkeeper);
        ICreature creature = new CreatureSpawner(NullLoggerFactory.Instance, world)
            .Spawn(new CreatureTemplateId(templateId));

        bool advertised = ObjectStateWriter.From(creature, GameEntityFields.CreatureUpdate).CanInteract == true;
        bool accepted = HandlerAccepts(world, creature);

        Assert.Equal(hasDialogue, advertised);
        Assert.Equal(accepted, advertised);
    }

    private static bool HandlerAccepts(IWorld world, ICreature creature)
    {
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(false);
        character.Name.Returns("Aldric");
        character.Position.Returns(creature.Position);
        character.InstanceId.Returns(Guid.NewGuid());

        var instance = Substitute.For<IMapInstance>();
        instance.Creatures.Returns(new Dictionary<ObjectGuid, ICreature> { [creature.Guid] = creature });
        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);
        world.InstanceRegistry.Returns(registry);

        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.Locale.Returns(AccountLocale.enUS);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

        new InteractHandler(NullLogger<InteractHandler>.Instance, world)
            .Execute(connection, new CInteractPacket { TargetGuid = creature.Guid.RawValue });

        bool sent = connection.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IWorldConnection.Send));
        Assert.Equal(sent, connection.CurrentDialogue is not null);

        return sent;
    }

    private static IWorld WorldWithDialogueFor(CreatureTemplateId talker)
    {
        var templates = Substitute.For<ICreatureTemplateRepository>();
        templates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate> { Template(Innkeeper, "Innkeeper"), Template(Wolf, "Wolf") }));

        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(
                [new CreatureBaseStat { Level = 1, Health = 50, DamageMin = 1, DamageMax = 2, Experience = 1 }]));

        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>(
                [new CreatureRarityModifier { Rarity = CreatureRarity.Normal, HealthMultiplier = 1f, DamageMultiplier = 1f, ExperienceMultiplier = 1f }]));

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>(
                [new DialogueNode { Id = new DialogueNodeId(1), CreatureTemplateId = talker, IsRoot = true, TextId = new LocalizedTextId(6) }]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var text = Substitute.For<ILocalizedTextRepository>();
        text.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>(
                [new LocalizedText { Id = new LocalizedTextId(6), Text = "Welcome." }]));
        text.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        text.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());
        var classStats = Substitute.For<IClassLevelStatRepository>();
        classStats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());
        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());
        var abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AbilityTemplate>());
        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));

        var data = new StaticData(createInfos, classStats, items, abilities, levels,
            templates, baseStats, rarities, text, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);
        data.LoadAsync().GetAwaiter().GetResult();

        var world = Substitute.For<IWorld>();
        world.Data.Returns(data);
        return world;
    }

    private static CreatureTemplate Template(CreatureTemplateId id, string name) => new()
    {
        Id = id,
        Name = name,
        MinLevel = 1,
        MaxLevel = 1,
        Rarity = CreatureRarity.Normal,
        HealthModifier = 1f,
        DamageModifier = 1f,
        ExperienceModifier = 1f
    };
}
