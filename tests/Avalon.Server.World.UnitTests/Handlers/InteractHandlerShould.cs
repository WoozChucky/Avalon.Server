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

public class InteractHandlerShould
{
    private static readonly ObjectGuid NpcGuid = new(ObjectType.Creature, 7);

    [Fact]
    public void Open_The_Conversation_At_The_Root_Node()
    {
        Fixture fixture = Fixture.WithTalkingNpc();

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        fixture.Connection.Received(1).Send(Arg.Any<NetworkPacket>());
        Assert.Equal((NpcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Drop_The_Packet_When_There_Is_No_Character()
    {
        Fixture fixture = Fixture.WithTalkingNpc();
        fixture.Connection.Character.Returns((ICharacter?)null);

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Drop_The_Packet_When_The_Character_Is_Dead()
    {
        Fixture fixture = Fixture.WithTalkingNpc();
        fixture.Character.IsDead.Returns(true);

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Drop_The_Packet_When_The_Target_Is_Not_In_The_Instance()
    {
        Fixture fixture = Fixture.WithTalkingNpc();

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = 999ul });

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Drop_The_Packet_When_The_Npc_Is_Dead()
    {
        Fixture fixture = Fixture.WithTalkingNpc();
        fixture.Npc.CurrentHealth.Returns(0u);

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Drop_The_Packet_When_The_Creature_Has_No_Dialogue()
    {
        // The ordinary case for every monster in the game.
        Fixture fixture = Fixture.WithSilentNpc();

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Drop_The_Packet_When_The_Player_Is_Out_Of_Range()
    {
        Fixture fixture = Fixture.WithTalkingNpc();
        fixture.Npc.Position.Returns(new Vector3(0, 0, 50));   // 50 m away, limit is 5

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        fixture.Connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        // A rejected interact must never leave a conversation "open" on the connection, even if the
        // rejection happens after CurrentDialogue would otherwise have been written.
        Assert.Null(fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Restart_At_The_Root_When_Interacting_Mid_Conversation()
    {
        // What a player expects from clicking an NPC twice, and it unwedges a client that lost the
        // window without needing a cancel packet.
        Fixture fixture = Fixture.WithTalkingNpc();
        fixture.Connection.CurrentDialogue = (NpcGuid, new DialogueNodeId(2));

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        Assert.Equal((NpcGuid, new DialogueNodeId(1)), fixture.Connection.CurrentDialogue);
    }

    [Fact]
    public void Interpolate_The_Players_Name_Into_The_Sent_Text()
    {
        // The catalog in this fixture is real, so this asserts the whole resolve-and-interpolate
        // path end to end rather than that a substitute was called. Capture the sent packet and
        // deserialize it.
        Fixture fixture = Fixture.WithTalkingNpc();

        fixture.Handler.Execute(fixture.Connection, new CInteractPacket { TargetGuid = NpcGuid.RawValue });

        SDialogueNodePacket sent = fixture.CaptureSentNode();

        Assert.Equal("Room's upstairs, Aldric.", sent.Text);
        Assert.Equal("Innkeeper", sent.SpeakerName);
        Assert.Equal(1, sent.NodeId);
        Assert.Equal("Farewell.", Assert.Single(sent.Options).Text);
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
            fixture.Npc.Guid.Returns(NpcGuid);
            fixture.Npc.Name.Returns("Innkeeper");
            fixture.Npc.CurrentHealth.Returns(100u);
            fixture.Npc.Position.Returns(new Vector3(0, 0, 2));
            fixture.Npc.TemplateId.Returns(new CreatureTemplateId(3));

            var instance = Substitute.For<IMapInstance>();
            instance.Creatures.Returns(new Dictionary<ObjectGuid, ICreature> { [NpcGuid] = fixture.Npc });

            var registry = Substitute.For<IInstanceRegistry>();
            registry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);

            var world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(registry);

            // world.Data is the concrete StaticData and cannot be substituted, so build a real one
            // over stubbed repositories — the arrangement ExperienceAwardShould already uses. The
            // catalogs it builds are real, which means this fixture also exercises the catalog code.
            var dialogueRepo = Substitute.For<IDialogueRepository>();
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

            var textRepo = Substitute.For<ILocalizedTextRepository>();
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
