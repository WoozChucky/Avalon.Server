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
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Handlers;

public class InteractHandlerShould
{
    private static readonly ObjectGuid s_npcGuid = new(ObjectType.Creature, 7);

    /// <summary>
    /// An interact opens the conversation at the NPC's root and sends that node with the player's name in its text.
    /// The catalog in this fixture is real, so this asserts the whole resolve-and-interpolate path end to end.
    /// </summary>
    [Fact]
    public void Open_the_conversation_at_the_root_and_interpolate_the_players_name()
    {
        var fixture = Fixture.WithTalkingNpc();

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = s_npcGuid.RawValue });

        Assert.Single(fixture.SentPackets);
        SDialogueNodePacket sent = fixture.CaptureSentNode();
        Assert.Equal("Room's upstairs, Aldric.", sent.Text);
        Assert.Equal("Innkeeper", sent.SpeakerName);
        Assert.Equal(1, sent.NodeId);
        Assert.Equal("Farewell.", Assert.Single(sent.Options).Text);
        Assert.Equal((s_npcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    /// <summary>
    /// An interact that cannot open a conversation is dropped without a reply and never leaves one open, even when
    /// it is refused after CurrentDialogue would have been written. A creature with no dialogue is every monster in
    /// the game. 6 m is inside the dialogue leash, but the leash only keeps an open conversation alive: starting one
    /// still needs the 5 m interact range.
    /// </summary>
    [Theory]
    [InlineData("dead character")]
    [InlineData("target not in the instance")]
    [InlineData("npc dead")]
    [InlineData("no dialogue")]
    [InlineData("past the interact range")]
    public void Drop_an_interact_that_cannot_open_a_conversation(string reason)
    {
        Fixture fixture = reason == "no dialogue" ? Fixture.WithSilentNpc() : Fixture.WithTalkingNpc();
        if (reason == "dead character")
            fixture.Character.IsDead.Returns(true);
        if (reason == "npc dead")
            fixture.Npc.CurrentHealth.Returns(0u);
        if (reason == "past the interact range")
            fixture.Npc.Position.Returns(new Vector3(0, 0, 6));

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket
        {
            TargetGuid = reason == "target not in the instance" ? 999ul : s_npcGuid.RawValue,
        });

        Assert.Empty(fixture.SentPackets);
        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Restart_At_The_Root_When_Interacting_Mid_Conversation()
    {
        // What a player expects from clicking an NPC twice, and it unwedges a client that lost the
        // window without needing a cancel packet.
        var fixture = Fixture.WithTalkingNpc();
        fixture.Connection.CurrentDialogue = (s_npcGuid, new DialogueNodeId(2));

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = s_npcGuid.RawValue });

        Assert.Equal((s_npcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    private sealed class Fixture
    {
        public IWorldConnection Connection = null!;
        public ICharacter Character = null!;
        public ICreature Npc = null!;
        public ILocalizedTextCatalog Text = null!;
        public InteractHandler Handler = null!;
        public List<NetworkPacket> SentPackets = null!;

        public static Fixture WithTalkingNpc() => Build(hasDialogue: true);
        public static Fixture WithSilentNpc() => Build(hasDialogue: false);

        /// <summary>
        /// Payload bytes are unencrypted: FakeAvalonCryptoSession.Encrypt is a pass-through, so what
        /// SDialogueNodePacket.Create wrote is exactly what protobuf-net reads back here. Mirrors
        /// CharacterSelectHandlerShould.DeserializeInventorySnapshot.
        /// </summary>
        public SDialogueNodePacket CaptureSentNode()
        {
            NetworkPacket packet = Assert.Single(
                SentPackets, p => p.Header.Type == NetworkPacketType.SMSG_DIALOGUE_NODE);
            using var stream = new MemoryStream(packet.Payload);
            return Serializer.Deserialize<SDialogueNodePacket>(stream);
        }

        private static Fixture Build(bool hasDialogue)
        {
            var fixture = new Fixture();

            fixture.Character = Substitute.For<ICharacter>();
            fixture.Character.IsDead.Returns(false);
            fixture.Character.Name.Returns("Aldric");
            fixture.Character.Class.Returns(CharacterClass.Warrior);
            fixture.Character.Gender.Returns(CharacterGender.Male);
            fixture.Character.Level.Returns((ushort)5);
            fixture.Character.Position.Returns(Vector3.zero);
            fixture.Character.InstanceId.Returns(Guid.NewGuid());

            fixture.Npc = Substitute.For<ICreature>();
            fixture.Npc.Guid.Returns(s_npcGuid);
            fixture.Npc.Name.Returns("Innkeeper");
            fixture.Npc.CurrentHealth.Returns(100u);
            fixture.Npc.Position.Returns(new Vector3(0, 0, 2));
            ICreatureMetadata npcMetadata = Substitute.For<ICreatureMetadata>();
            npcMetadata.Id.Returns(new CreatureTemplateId(3));
            fixture.Npc.Metadata.Returns(npcMetadata);

            IMapInstance instance = Substitute.For<IMapInstance>();
            instance.Creatures.Returns(new Dictionary<ObjectGuid, ICreature> { [s_npcGuid] = fixture.Npc });

            IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
            registry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);

            IWorld world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(registry);

            // world.Data is the concrete StaticData and cannot be substituted, so build a real one
            // over stubbed repositories — the arrangement ExperienceAwardShould already uses. The
            // catalogs it builds are real, which means this fixture also exercises the catalog code.
            IDialogueRepository dialogueRepo = Substitute.For<IDialogueRepository>();
            dialogueRepo.GetAllNodesAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<DialogueNode>>(hasDialogue
                    ? [new DialogueNode
                        {
                            Id = new DialogueNodeId(1),
                            CreatureTemplateId = new CreatureTemplateId(3),
                            IsRoot = true,
                            TextId = new LocalizedTextId(6)
                        }]
                    : []));
            dialogueRepo.GetAllOptionsAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<DialogueOption>>(hasDialogue
                    ? [new DialogueOption
                        {
                            Id = new DialogueOptionId(9),
                            NodeId = new DialogueNodeId(1),
                            TextId = new LocalizedTextId(10),
                            NextNodeId = null,
                            SortOrder = 0
                        }]
                    : []));

            ILocalizedTextRepository textRepo = Substitute.For<ILocalizedTextRepository>();
            textRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<LocalizedText>>(
                    [new LocalizedText { Id = new LocalizedTextId(6), Text = "Room's upstairs, {name}." },
                     new LocalizedText { Id = new LocalizedTextId(10), Text = "Farewell." }]));
            textRepo.GetAllLocalesAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
            textRepo.GetAllClassNamesAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

            // Remaining StaticData repositories are stubbed empty.
            StaticData data = BuildStaticData(textRepo, dialogueRepo);
            data.LoadAsync().GetAwaiter().GetResult();
            world.Data.Returns(data);

            fixture.Text = data.LocalizedTexts;

            fixture.Connection = Substitute.For<IWorldConnection>();
            fixture.Connection.Character.Returns(fixture.Character);
            fixture.Connection.Locale.Returns(AccountLocale.enUS);
            fixture.Connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

            var sentPackets = new List<NetworkPacket>();
            fixture.Connection.When(c => c.Send(Arg.Any<NetworkPacket>()))
                .Do(ci => sentPackets.Add(ci.Arg<NetworkPacket>()));
            fixture.SentPackets = sentPackets;

            fixture.Handler = new InteractHandler(NullLogger<InteractHandler>.Instance, world);

            return fixture;
        }

        private static StaticData BuildStaticData(
            ILocalizedTextRepository textRepo, IDialogueRepository dialogueRepo)
        {
            ICharacterCreateInfoRepository createInfos = Substitute.For<ICharacterCreateInfoRepository>();
            createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());

            IClassLevelStatRepository stats = Substitute.For<IClassLevelStatRepository>();
            stats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());

            IItemTemplateRepository items = Substitute.For<IItemTemplateRepository>();
            items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());

            IAbilityTemplateRepository abilities = Substitute.For<IAbilityTemplateRepository>();
            abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AbilityTemplate>());

            ICharacterLevelExperienceRepository levels = Substitute.For<ICharacterLevelExperienceRepository>();
            levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));

            ICreatureTemplateRepository creatureTemplates = Substitute.For<ICreatureTemplateRepository>();
            creatureTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new List<CreatureTemplate>()));

            ICreatureBaseStatRepository baseStats = Substitute.For<ICreatureBaseStatRepository>();
            baseStats.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(
                    [new CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));

            ICreatureRarityModifierRepository rarities = Substitute.For<ICreatureRarityModifierRepository>();
            rarities.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>([]));

            return new StaticData(createInfos, stats, items, abilities, levels,
                creatureTemplates, baseStats, rarities,
                textRepo, dialogueRepo, LootRepositories.Empty(), NullLoggerFactory.Instance);
        }
    }
}
