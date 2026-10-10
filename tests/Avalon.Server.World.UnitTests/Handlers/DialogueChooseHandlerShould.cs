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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class DialogueChooseHandlerShould
{
    private static readonly ObjectGuid s_npcGuid = new(ObjectType.Creature, 7);

    /// <summary>
    /// The leash (#678) is inclusive and wider than the 5 m that opening a conversation needs, so stepping back
    /// mid-sentence keeps the window: at 5.5 m and at exactly 6 m the choice advances to the next node.
    /// </summary>
    [Theory]
    [InlineData(5.5f)]
    [InlineData(6f)]
    public void Advance_to_the_next_node_within_the_leash(float distance)
    {
        var fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (s_npcGuid, new DialogueNodeId(1));
        fixture.Npc.Position.Returns(new Vector3(0, 0, distance));

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 1));

        Assert.Equal((s_npcGuid, new DialogueNodeId(2)), fixture.Connection.CurrentDialogue);
        OutboundPacket sent = Assert.Single(fixture.SentPackets);
        Assert.Equal(NetworkPacketType.SMSG_DIALOGUE_NODE, sent.Header.Type);
    }

    /// <summary>
    /// A choice the open conversation does not offer is dropped and changes nothing. The first row is the one this
    /// design exists for: option 4 is real but belongs to node 2 while the server shows node 1, and a handler that
    /// looked options up globally instead of on the open node would accept it. Rejecting it is why the conversation
    /// is server-authoritative rather than a graph shipped to the client.
    /// </summary>
    [Theory]
    [InlineData(true, false, 7u, 1, 4)]    // an option of another node
    [InlineData(true, false, 7u, 2, 1)]    // a node the server is not showing
    [InlineData(true, false, 8u, 1, 1)]    // another NPC
    [InlineData(false, false, 7u, 1, 1)]   // no conversation open
    [InlineData(true, true, 7u, 1, 1)]     // a dead character
    public void Ignore_a_choice_the_open_conversation_does_not_offer(bool open, bool dead, uint npc, int node, int option)
    {
        var fixture = Fixture.Build();
        (ObjectGuid, DialogueNodeId)? conversation = open ? (s_npcGuid, new DialogueNodeId(1)) : null;
        fixture.Connection.CurrentDialogue = conversation;
        fixture.Character.IsDead.Returns(dead);

        fixture.Handler.Execute(fixture.Connection, new CDialogueChoosePacket
        {
            TargetGuid = new ObjectGuid(ObjectType.Creature, npc).RawValue,
            NodeId = node,
            OptionId = option,
        });

        Assert.Empty(fixture.SentPackets);
        Assert.Equal(conversation, fixture.Connection.CurrentDialogue);
    }

    /// <summary>
    /// The conversation ends out loud, so the client's window closes, when the NPC died or was removed meanwhile (a
    /// player must not keep talking to a corpse), when the option leads to an unknown node (broken content closes the
    /// window rather than wedging it open), when the open node was authored for another creature (a guid reused
    /// across a despawn and respawn must not hand out dialogue never meant for this NPC), and when the player has
    /// walked just past the 6 m leash or stands at a NaN distance, which compares false against everything and so
    /// must fail closed.
    /// </summary>
    [Theory]
    [InlineData("npc dead")]
    [InlineData("npc gone")]
    [InlineData("unknown next node")]
    [InlineData("node of another creature")]
    [InlineData("past the leash")]
    [InlineData("NaN distance")]
    public void End_the_conversation_out_loud(string reason)
    {
        var fixture = Fixture.Build(npcInInstance: reason != "npc gone");
        int node = reason == "node of another creature" ? 3 : 1;
        fixture.Connection.CurrentDialogue = (s_npcGuid, new DialogueNodeId(node));
        if (reason == "npc dead")
            fixture.Npc.CurrentHealth.Returns(0u);
        if (reason == "past the leash")
            fixture.Npc.Position.Returns(new Vector3(0, 0, 6.01f));
        if (reason == "NaN distance")
            fixture.Character.Position.Returns(new Vector3(float.NaN, 0, 0));

        fixture.Handler.Execute(fixture.Connection, Choose(node, option: reason == "unknown next node" ? 3 : 1));

        Assert.Null(fixture.Connection.CurrentDialogue);
        OutboundPacket sent = Assert.Single(fixture.SentPackets);
        Assert.Equal(NetworkPacketType.SMSG_DIALOGUE_END, sent.Header.Type);
    }

    private static CDialogueChoosePacket Choose(int node, int option)
        => new() { TargetGuid = s_npcGuid.RawValue, NodeId = node, OptionId = option };

    private sealed class Fixture
    {
        public IWorldConnection Connection = null!;
        public ICharacter Character = null!;
        public ICreature Npc = null!;
        public DialogueChooseHandler Handler = null!;
        public List<OutboundPacket> SentPackets = null!;

        public static Fixture Build(bool npcInInstance = true)
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
            instance.Creatures.Returns(npcInInstance
                ? new Dictionary<ObjectGuid, ICreature> { [s_npcGuid] = fixture.Npc }
                : new Dictionary<ObjectGuid, ICreature>());

            IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
            registry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);

            IWorld world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(registry);

            // world.Data is the concrete StaticData and cannot be substituted, so build a real one
            // over stubbed repositories — the arrangement InteractHandlerShould already uses. The
            // catalogs it builds are real, which means this fixture also exercises the catalog code.
            IDialogueRepository dialogueRepo = Substitute.For<IDialogueRepository>();
            dialogueRepo.GetAllNodesAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<DialogueNode>>(
                    [
                        new DialogueNode
                        {
                            Id = new DialogueNodeId(1),
                            CreatureTemplateId = new CreatureTemplateId(3),
                            IsRoot = true,
                            TextId = new LocalizedTextId(6)
                        },
                        new DialogueNode
                        {
                            Id = new DialogueNodeId(2),
                            CreatureTemplateId = new CreatureTemplateId(3),
                            IsRoot = false,
                            TextId = new LocalizedTextId(7)
                        },
                        new DialogueNode
                        {
                            // Real node, but authored for a different creature template than
                            // NpcGuid resolves to (3) — the cross-linked-content case.
                            Id = new DialogueNodeId(3),
                            CreatureTemplateId = new CreatureTemplateId(99),
                            IsRoot = false,
                            TextId = new LocalizedTextId(8)
                        }
                    ]));
            dialogueRepo.GetAllOptionsAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<DialogueOption>>(
                    [
                        new DialogueOption
                        {
                            Id = new DialogueOptionId(1),
                            NodeId = new DialogueNodeId(1),
                            TextId = new LocalizedTextId(10),
                            NextNodeId = new DialogueNodeId(2),
                            SortOrder = 0
                        },
                        new DialogueOption
                        {
                            Id = new DialogueOptionId(2),
                            NodeId = new DialogueNodeId(1),
                            TextId = new LocalizedTextId(11),
                            NextNodeId = null,
                            SortOrder = 1
                        },
                        new DialogueOption
                        {
                            Id = new DialogueOptionId(3),
                            NodeId = new DialogueNodeId(1),
                            TextId = new LocalizedTextId(12),
                            NextNodeId = new DialogueNodeId(77), // not among the nodes: broken content
                            SortOrder = 2
                        },
                        new DialogueOption
                        {
                            Id = new DialogueOptionId(4),
                            NodeId = new DialogueNodeId(2),
                            TextId = new LocalizedTextId(13),
                            NextNodeId = null,
                            SortOrder = 0
                        }
                    ]));

            ILocalizedTextRepository textRepo = Substitute.For<ILocalizedTextRepository>();
            textRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<LocalizedText>>(
                    [
                        new LocalizedText { Id = new LocalizedTextId(6), Text = "Room's upstairs, {name}." },
                        new LocalizedText { Id = new LocalizedTextId(7), Text = "Anything else?" },
                        new LocalizedText { Id = new LocalizedTextId(10), Text = "Tell me more." },
                        new LocalizedText { Id = new LocalizedTextId(11), Text = "Farewell." },
                        new LocalizedText { Id = new LocalizedTextId(12), Text = "What's upstairs?" },
                        new LocalizedText { Id = new LocalizedTextId(13), Text = "Goodbye." }
                    ]));
            textRepo.GetAllLocalesAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
            textRepo.GetAllClassNamesAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

            // Remaining StaticData repositories are stubbed empty.
            StaticData data = BuildStaticData(textRepo, dialogueRepo);
            data.LoadAsync().GetAwaiter().GetResult();
            world.Data.Returns(data);

            fixture.Connection = Substitute.For<IWorldConnection>();
            fixture.Connection.Character.Returns(fixture.Character);
            fixture.Connection.Locale.Returns(AccountLocale.enUS);
            fixture.Connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

            var sentPackets = new List<OutboundPacket>();
            fixture.Connection.When(c => c.Send(Arg.Any<OutboundPacket>()))
                .Do(ci => sentPackets.Add(ci.Arg<OutboundPacket>()));
            fixture.SentPackets = sentPackets;

            fixture.Handler = new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, world);

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
