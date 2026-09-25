using System.IO;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
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
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class DialogueChooseHandlerShould
{
    private static readonly ObjectGuid NpcGuid = new(ObjectType.Creature, 7);

    [Fact]
    public void Advance_To_The_Next_Node()
    {
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 1));

        Assert.Equal((NpcGuid, new DialogueNodeId(2)), fixture.Connection.CurrentDialogue);
        fixture.Connection.Received(1).Send(Arg.Any<NetworkPacket>());
        NetworkPacket sent = Assert.Single(fixture.SentPackets);
        Assert.Equal(NetworkPacketType.SMSG_DIALOGUE_NODE, sent.Header.Type);
    }

    [Fact]
    public void End_The_Conversation_On_A_Null_Next_Node()
    {
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 2));

        Assert.Null(fixture.Connection.CurrentDialogue);
        fixture.Connection.Received(1).Send(Arg.Any<NetworkPacket>());
        NetworkPacket sent = Assert.Single(fixture.SentPackets);
        Assert.Equal(NetworkPacketType.SMSG_DIALOGUE_END, sent.Header.Type);
    }

    [Fact]
    public void Reject_An_Option_Belonging_To_A_Different_Node()
    {
        // THE test this design exists for. A valid OptionId from another node is exactly what a
        // happy-path test cannot see, and rejecting it is the entire reason the conversation is
        // server-authoritative rather than a graph shipped to the client.
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));

        // Option 4 is real and belongs to node 2 — the server is showing node 1. A handler that
        // looked options up globally instead of on the open node would happily accept it.
        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 4));

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Equal((NpcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Reject_A_Choice_Against_A_Node_The_Server_Is_Not_Showing()
    {
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));

        fixture.Handler.Execute(fixture.Connection, Choose(node: 2, option: 1));

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Equal((NpcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Reject_A_Choice_When_No_Conversation_Is_Open()
    {
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = null;

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 1));

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Reject_A_Choice_Naming_A_Different_Npc()
    {
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));

        var other = new ObjectGuid(ObjectType.Creature, 8);
        fixture.Handler.Execute(fixture.Connection,
            new CDialogueChoosePacket { TargetGuid = other.RawValue, NodeId = 1, OptionId = 1 });

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Equal((NpcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void End_The_Conversation_When_The_Npc_Has_Died_Meanwhile()
    {
        // Review Focus 1. Someone else killed the innkeeper mid-sentence; the player must not keep
        // talking to a corpse.
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));
        fixture.Npc.CurrentHealth.Returns(0u);

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 1));

        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void End_The_Conversation_When_The_Npc_Has_Left_The_Instance()
    {
        // Review Focus 1, despawn variant — corpse removal takes the creature out entirely.
        Fixture fixture = Fixture.Build(npcInInstance: false);
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 1));

        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Treat_An_Unknown_Next_Node_As_An_End()
    {
        // Broken content should close the window, not wedge it open.
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 3));

        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Drop_The_Packet_When_There_Is_No_Character()
    {
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));
        fixture.Connection.Character.Returns((ICharacter?)null);

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 1));

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Equal((NpcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Drop_The_Packet_When_The_Character_Is_Dead()
    {
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(1));
        fixture.Character.IsDead.Returns(true);

        fixture.Handler.Execute(fixture.Connection, Choose(node: 1, option: 1));

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Equal((NpcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void End_The_Conversation_When_The_Open_Node_Belongs_To_A_Different_Creature()
    {
        // Guards against an ObjectGuid reused across a despawn/respawn: node 3 is real but was
        // authored for a different creature template than the one now answering to NpcGuid, so
        // advancing on it would hand out dialogue that was never meant for this NPC.
        Fixture fixture = Fixture.Build();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(3));

        fixture.Handler.Execute(fixture.Connection, Choose(node: 3, option: 1));

        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    private static CDialogueChoosePacket Choose(int node, int option)
        => new() { TargetGuid = NpcGuid.RawValue, NodeId = node, OptionId = option };

    private sealed class Fixture
    {
        public IWorldConnection Connection = null!;
        public ICharacter Character = null!;
        public ICreature Npc = null!;
        public DialogueChooseHandler Handler = null!;
        public List<NetworkPacket> SentPackets = null!;

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
            fixture.Npc.Guid.Returns(NpcGuid);
            fixture.Npc.Name.Returns("Innkeeper");
            fixture.Npc.CurrentHealth.Returns(100u);
            fixture.Npc.Position.Returns(new Vector3(0, 0, 2));
            var npcMetadata = Substitute.For<ICreatureMetadata>();
            npcMetadata.Id.Returns(new CreatureTemplateId(3));
            fixture.Npc.Metadata.Returns(npcMetadata);

            var instance = Substitute.For<IMapInstance>();
            instance.Creatures.Returns(npcInInstance
                ? new Dictionary<ObjectGuid, ICreature> { [NpcGuid] = fixture.Npc }
                : new Dictionary<ObjectGuid, ICreature>());

            var registry = Substitute.For<IInstanceRegistry>();
            registry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);

            var world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(registry);

            // world.Data is the concrete StaticData and cannot be substituted, so build a real one
            // over stubbed repositories — the arrangement InteractHandlerShould already uses. The
            // catalogs it builds are real, which means this fixture also exercises the catalog code.
            var dialogueRepo = Substitute.For<IDialogueRepository>();
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

            var textRepo = Substitute.For<ILocalizedTextRepository>();
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

            var sentPackets = new List<NetworkPacket>();
            fixture.Connection.When(c => c.Send(Arg.Any<NetworkPacket>()))
                .Do(ci => sentPackets.Add(ci.Arg<NetworkPacket>()));
            fixture.SentPackets = sentPackets;

            fixture.Handler = new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, world);

            return fixture;
        }

        private static StaticData BuildStaticData(
            ILocalizedTextRepository textRepo, IDialogueRepository dialogueRepo)
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
            levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));

            var baseStats = Substitute.For<ICreatureBaseStatRepository>();
            baseStats.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>([]));

            var rarities = Substitute.For<ICreatureRarityModifierRepository>();
            rarities.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>([]));

            return new StaticData(createInfos, stats, items, abilities, levels, baseStats, rarities,
                textRepo, NullLoggerFactory.Instance, dialogueRepo);
        }
    }
}
