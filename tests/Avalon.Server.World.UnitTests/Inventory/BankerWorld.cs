using System.IO;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// A real warrior at the origin of a town instance holding a banker (template 11, 3 m away, whose
/// root offers "Open my bank." then "Farewell.") and an innkeeper (template 3, 2 m away, no bank).
/// The warrior's stats are already derived at level 1: 240 health, 100 power.
/// </summary>
internal sealed class BankerWorld
{
    public static readonly ObjectGuid BankerGuid = new(ObjectType.Creature, 90);
    public static readonly ObjectGuid StrangerGuid = new(ObjectType.Creature, 91);
    public static readonly CreatureTemplateId BankerTemplate = new(11);
    public static readonly CreatureTemplateId StrangerTemplate = new(3);

    public const int BankerRoot = 1;
    public const int StrangerRoot = 2;
    public const int OpenBankOption = 1;
    public const int FarewellOption = 2;

    public static readonly ClassLevelStat WarriorLevel1 = new()
    {
        Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, BaseMana = 0,
        Stamina = 22, Strength = 23, Agility = 20, Intellect = 20,
    };

    public CharacterEntity Character { get; } = TestCharacters.New(id: 7);
    public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection>();
    public IWorld World { get; } = Substitute.For<IWorld>();
    public Dictionary<ObjectGuid, ICreature> Creatures { get; } = [];
    public List<NetworkPacket> Sent { get; } = [];
    public StaticData Data { get; }
    public ICreature Banker { get; }
    public ICreature Stranger { get; }

    public BankerWorld()
    {
        Data = TestStaticData.LoadAsync(
            classStats: [WarriorLevel1],
            items: EquipTemplates.All,
            nodes:
            [
                new DialogueNode { Id = BankerRoot, CreatureTemplateId = BankerTemplate, IsRoot = true, TextId = 1 },
                new DialogueNode { Id = StrangerRoot, CreatureTemplateId = StrangerTemplate, IsRoot = true, TextId = 4 },
            ],
            options:
            [
                new DialogueOption
                {
                    Id = OpenBankOption, NodeId = BankerRoot, TextId = 2, NextNodeId = BankerRoot, SortOrder = 0,
                    Action = DialogueOptionAction.OpenBank,
                },
                new DialogueOption { Id = FarewellOption, NodeId = BankerRoot, TextId = 3, NextNodeId = null, SortOrder = 1 },
                new DialogueOption { Id = 3, NodeId = StrangerRoot, TextId = 3, NextNodeId = null, SortOrder = 0 },
            ],
            texts:
            [
                new LocalizedText { Id = 1, Text = "Coin and keepsakes, {name}." },
                new LocalizedText { Id = 2, Text = "Open my bank." },
                new LocalizedText { Id = 3, Text = "Farewell." },
                new LocalizedText { Id = 4, Text = "Room's upstairs." },
            ]).GetAwaiter().GetResult();

        Character.InstanceId = new Guid("46300000-0000-0000-0000-000000000463");
        Banker = Npc(BankerGuid, BankerTemplate, "Marta Ledgerwell", new Vector3(0, 0, 3));
        Stranger = Npc(StrangerGuid, StrangerTemplate, "Innkeeper", new Vector3(0, 0, 2));

        // Only the character's own instance is stubbed; any other id comes back null.
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.Creatures.Returns(Creatures);
        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(Arg.Any<Guid>()).Returns((IMapInstance?)null);
        registry.GetInstanceById(Character.InstanceId).Returns(instance);

        World.InstanceRegistry.Returns(registry);
        World.Configuration.Returns(new GameConfiguration());
        World.Data.Returns(Data);

        Connection.Character.Returns(Character);
        Connection.Locale.Returns(AccountLocale.enUS);
        Connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        Connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => Sent.Add(ci.Arg<NetworkPacket>()));

        CharacterStatsRefresh.Apply(Character, Data, CurrentValues.Refill);
    }

    private ICreature Npc(ObjectGuid guid, CreatureTemplateId template, string name, Vector3 at)
    {
        var metadata = Substitute.For<ICreatureMetadata>();
        metadata.Id.Returns(template);
        var npc = Substitute.For<ICreature>();
        npc.Guid.Returns(guid);
        npc.Name.Returns(name);
        npc.CurrentHealth.Returns(100u);
        npc.Position.Returns(at);
        npc.Metadata.Returns(metadata);
        Creatures[guid] = npc;
        return npc;
    }

    /// <summary>What choosing "Open my bank." leaves behind, without going through the handler.</summary>
    public void OpenBank()
    {
        Connection.CurrentDialogue = (BankerGuid, new DialogueNodeId(BankerRoot));
        Character.OpenBankNpc = BankerGuid;
    }

    public List<T> Read<T>(NetworkPacketType type) =>
        Sent.Where(p => p.Header.Type == type).Select(p =>
        {
            using var stream = new MemoryStream(p.Payload);
            return Serializer.Deserialize<T>(stream);
        }).ToList();
}
