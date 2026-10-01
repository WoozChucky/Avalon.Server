using System.IO;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>A loot random that always answers the same number: 0 makes every roll succeed, 0.999 every roll above 99.9 % fail.</summary>
internal sealed class SteadyLootRandom(double value) : ILootRandom
{
    public double Value { get; set; } = value;
    public double NextDouble() => Value;
    public long NextInt64(long minInclusive, long maxExclusive) => minInclusive;
}

/// <summary>A character in the quest test world, and every packet its connection was sent.</summary>
internal sealed record QuestClient(IWorldConnection Connection, CharacterEntity Character, List<NetworkPacket> Sent)
{
    public uint Id => Character.Guid.Id;

    public List<T> Read<T>(NetworkPacketType type) => Sent
        .Where(p => p.Header.Type == type)
        .Select(p =>
        {
            using var stream = new MemoryStream(p.Payload);
            return Serializer.Deserialize<T>(stream);
        })
        .ToList();

    public List<string> Lines() => Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)
        .Where(m => m.Channel == ChatChannel.System).Select(m => m.Message).ToList();

    public void Clear() => Sent.Clear();
}

/// <summary>
/// A loaded StaticData holding QuestTestData's creatures, items, texts, dialogue and quests; a world over it with
/// one instance whose creatures the test places; characters joined through recording connections; and a real
/// QuestService over a manual clock. No MapInstance: the instance is a substitute, so a test drives the service
/// directly, and the tests that need a real kill build a MapInstance with TestMapInstances and this world.
/// </summary>
internal sealed class QuestTestWorld
{
    public const int GiverRoot = 7401, EnderRoot = 7402, TalkRoot = 7403;

    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];
    private uint _nextCreature = 950_000;

    private QuestTestWorld(StaticData data, GameConfiguration config, IServiceProvider services, SteadyLootRandom random, PartyService? parties)
    {
        Data = data;
        Config = config;
        Random = random;
        World = Substitute.For<IWorld>();
        World.Data.Returns(data);
        World.Configuration.Returns(config);
        World.MapTemplates.Returns(new List<MapTemplate>());

        Instance = Substitute.For<IMapInstance>();
        Instance.InstanceId.Returns(Guid.NewGuid());
        Instance.Creatures.Returns(_creatures);
        World.InstanceRegistry.GetInstanceById(Arg.Any<Guid>()).Returns(ci => ci.Arg<Guid>() == Instance.InstanceId ? Instance : null);

        Economy = new CharacterEconomy(World, new ItemIdAllocator());
        Quests = new QuestService(World, services, Economy, random, Clock, NullLogger<QuestService>.Instance, parties);
    }

    public ManualTimerClock Clock { get; } = new();
    public GameConfiguration Config { get; }
    public StaticData Data { get; }
    public IWorld World { get; }
    public IMapInstance Instance { get; }
    public ICharacterEconomy Economy { get; }
    public SteadyLootRandom Random { get; }
    public QuestService Quests { get; }

    public static async Task<QuestTestWorld> CreateAsync(
        List<QuestTemplate>? quests = null,
        Action<GameConfiguration>? configure = null,
        IScriptManager? scripts = null,
        IServiceProvider? services = null,
        PartyService? parties = null,
        List<CharacterLevelExperience>? levels = null)
    {
        List<QuestTemplate> rows = quests ?? Chain();
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            items: Items,
            creatures: QuestTestData.Creatures,
            texts: Texts,
            nodes: () =>
            [
                new DialogueNode { Id = GiverRoot, CreatureTemplateId = Giver, IsRoot = true, TextId = BodyText },
                new DialogueNode { Id = EnderRoot, CreatureTemplateId = Ender, IsRoot = true, TextId = BodyText },
                new DialogueNode { Id = TalkRoot, CreatureTemplateId = TalkTarget, IsRoot = true, TextId = BodyText },
            ],
            options: () =>
            [
                new DialogueOption { Id = 7411, NodeId = GiverRoot, TextId = DoneText, NextNodeId = null, SortOrder = 0 },
                new DialogueOption { Id = 7412, NodeId = EnderRoot, TextId = DoneText, NextNodeId = null, SortOrder = 0 },
                new DialogueOption { Id = 7413, NodeId = TalkRoot, TextId = DoneText, NextNodeId = null, SortOrder = 0 },
            ],
            levels: () => levels ??
            [
                new CharacterLevelExperience { Level = 1, Experience = 400 },
                new CharacterLevelExperience { Level = 2, Experience = 900 },
                new CharacterLevelExperience { Level = 3, Experience = 1400 },
            ],
            quests: QuestRepositories.Of(() => rows),
            scripts: scripts));

        var config = new GameConfiguration();
        configure?.Invoke(config);
        return new QuestTestWorld(data, config, services ?? Substitute.For<IServiceProvider>(), new SteadyLootRandom(0), parties);
    }

    /// <summary>A living NPC (or monster) of this template in the instance, at the position given.</summary>
    public Creature Place(ulong template, Vector3? position = null)
    {
        CreatureTemplate metadata = Data.CreatureTemplates.Single(t => t.Id.Value == template);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, _nextCreature++),
            Metadata = metadata,
            TemplateId = metadata.Id,
            Name = metadata.Name,
            Position = position ?? Vector3.zero,
            Health = 10,
            CurrentHealth = 10,
        };
        _creatures[creature.Guid] = creature;
        return creature;
    }

    public void Remove(ICreature creature) => _creatures.Remove(creature.Guid);

    /// <summary>Character <paramref name="id" /> at the origin of the instance, alive.</summary>
    public QuestClient Join(uint id = 1, ushort level = 1, CharacterClass characterClass = CharacterClass.Warrior, ulong money = 0)
    {
        CharacterEntity character = TestCharacters.New(id, money);
        character.Level = level;
        character.Data!.Class = characterClass;
        character.InstanceId = Instance.InstanceId;
        character.Position = Vector3.zero;

        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.AccountId.Returns(new AccountId(id));
        connection.Locale.Returns(AccountLocale.enUS);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));
        return new QuestClient(connection, character, sent);
    }

    /// <summary>Opens a conversation between the client and the NPC at the NPC's root, as InteractHandler would.</summary>
    public void Talk(QuestClient client, ICreature npc)
    {
        DialogueNodeId root = Data.Dialogue.GetRoot(npc.Metadata.Id)!.Id;
        client.Connection.CurrentDialogue = (npc.Guid, root);
    }

    /// <summary>Accepts a quest at its giver: places the giver, opens the conversation, accepts, and asserts Ok.</summary>
    public void Accept(QuestClient client, uint questId)
    {
        Assert.True(Data.Quests.TryGet(questId, out QuestView? quest));
        Creature giver = Place(quest!.GiverCreatureId.Value);
        Talk(client, giver);
        Assert.Equal(Avalon.Network.Packets.Quest.QuestResult.Ok, Quests.Accept(client.Connection, client.Character, questId, giver.Guid.RawValue));
        Remove(giver);
    }

    /// <summary>Marks a quest completed in the character's log, as a turn-in would have.</summary>
    public static void Complete(QuestClient client, uint questId) => client.Character.Quests.Complete(questId, DateTime.UnixEpoch);
}
