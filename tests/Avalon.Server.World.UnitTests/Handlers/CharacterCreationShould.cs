using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using ProtoBuf;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// Character creation driven end to end: the real handler, the real repositories, two real
/// databases, and the continuation chain pumped the way the tick loop pumps it. The five writes
/// each land on their own context, which is the condition the defect this covers needs.
/// </summary>
public class CharacterCreationShould : IDisposable
{
    private readonly SqliteDatabase<CharacterDbContext> _characters = SqliteDatabase.Characters();
    private readonly SqliteDatabase<WorldDbContext> _world = SqliteDatabase.World();

    [Fact]
    public async Task Persist_the_character_its_stats_abilities_and_items()
    {
        StaticData data = await LoadStaticDataAsync();
        CharacterCreateInfo createInfo = data.CharacterCreateInfos.First();

        IWorldConnection connection = NewConnection();
        CharacterCreateHandler handler = NewHandler(data);

        handler.Execute(connection, new CCharacterCreatePacket
        {
            Name = "Testarossa",
            Class = (int)createInfo.Class,
        });

        await PumpAsync(connection);

        await using CharacterDbContext characterDb = _characters.CreateDbContext();
        Avalon.Domain.Characters.Character character =
            await characterDb.Characters.SingleAsync(c => c.Name == "Testarossa");

        Assert.Equal(1, await characterDb.CharacterStats.CountAsync(s => s.CharacterId == character.Id));
        Assert.Equal(createInfo.StartingSpells.Count,
            await characterDb.CharacterAbilities.CountAsync(a => a.CharacterId == character.Id));
        Assert.Equal(createInfo.StartingItems.Count,
            await characterDb.CharacterInventory.CountAsync(i => i.CharacterId == character.Id));

        // The item instances now live beside the slots that point at them, and each carries an id
        // the server allocated.
        List<ItemInstance> items = await characterDb.ItemInstances.AsNoTracking()
            .Where(i => i.CharacterId == character.Id).ToListAsync();
        Assert.Equal(createInfo.StartingItems.Count, items.Count);
        Assert.All(items, item => Assert.NotEqual(Guid.Empty, item.Id.Value));

        // StaticData's cached templates were not written back into the World database.
        await using WorldDbContext worldDb = _world.CreateDbContext();
        Assert.Equal(createInfo.StartingItems.Distinct().Count(),
            await worldDb.ItemTemplates.CountAsync(t => createInfo.StartingItems.Contains(t.Id)));
    }

    /// <summary>
    /// Creation derives the new character's maximums and its stats row from the seeded level 1
    /// row with nothing worn: every starting item goes to the Bag. The Warrior row is a regression
    /// guard: the old GetBase* helpers already produced these values for a Warrior, so it passed
    /// before the calculator was wired; the other three classes did not.
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Warrior, "Warrioress", 240, 100, 46u, 4u, 5.0f)]
    [InlineData(CharacterClass.Wizard, "Wizardess", 121, 365, 10u, 69u, 0f)]
    [InlineData(CharacterClass.Hunter, "Huntress", 178, 68, 45u, 10u, 0f)]
    [InlineData(CharacterClass.Healer, "Healeress", 158, 296, 10u, 46u, 0f)]
    public async Task Derive_a_new_characters_maximums_and_stats_from_its_level_one_row(
        CharacterClass @class, string name, int health, int power, uint attack, uint ability, float block)
    {
        StaticData data = await LoadStaticDataAsync();
        IWorldConnection connection = NewConnection();

        NewHandler(data).Execute(connection, new CCharacterCreatePacket { Name = name, Class = (int)@class });
        await PumpAsync(connection);

        await using CharacterDbContext characterDb = _characters.CreateDbContext();
        Avalon.Domain.Characters.Character character =
            await characterDb.Characters.AsNoTracking().SingleAsync(c => c.Name == name);
        Assert.Equal((health, power), (character.Health, character.Power1));

        Avalon.Domain.Characters.CharacterStats stats =
            await characterDb.CharacterStats.AsNoTracking().SingleAsync(s => s.CharacterId == character.Id);
        Assert.Equal(((uint)health, (uint)power), (stats.MaxHealth, stats.MaxPower1));
        Assert.Equal((attack, ability, block), (stats.AttackDamage, stats.AbilityDamage, stats.BlockPct));
        Assert.Equal(0u, stats.Armor);
    }

    /// <summary>
    /// Every principal the handler writes is named by its foreign key alone. A navigation would
    /// point at a row that already exists — the character the previous call returned, or the
    /// template StaticData has cached since startup — and insert it a second time.
    /// </summary>
    [Fact]
    public async Task Name_principals_by_foreign_key_and_not_by_navigation()
    {
        StaticData data = await LoadStaticDataAsync();
        CharacterCreateInfo createInfo = data.CharacterCreateInfos.First();

        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByAccountAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Domain.Characters.Character>()));
        characters.CreateAsync(Arg.Any<Domain.Characters.Character>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Domain.Characters.Character created = call.Arg<Domain.Characters.Character>();
                created.Id = new CharacterId(1);
                return Task.FromResult(created);
            });

        ICharacterStatsRepository stats = Substitute.For<ICharacterStatsRepository>();
        stats.CreateAsync(Arg.Any<Domain.Characters.CharacterStats>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<Domain.Characters.CharacterStats>()));

        ICharacterAbilityRepository abilities = Substitute.For<ICharacterAbilityRepository>();
        abilities.CreateAsync(Arg.Any<IList<Domain.Characters.CharacterAbility>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<IList<Domain.Characters.CharacterAbility>>()));

        ICharacterInventoryRepository inventory = Substitute.For<ICharacterInventoryRepository>();
        inventory.CreateAsync(Arg.Any<IList<Domain.Characters.CharacterInventory>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<IList<Domain.Characters.CharacterInventory>>()));

        IItemInstanceRepository items = Substitute.For<IItemInstanceRepository>();
        items.CreateAsync(Arg.Any<List<ItemInstance>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<List<ItemInstance>>()));

        IWorldConnection connection = NewConnection();
        CharacterCreateHandler handler = new(
            NullLogger<CharacterCreateHandler>.Instance,
            characters, stats, abilities, inventory, items, new ItemIdAllocator(), NewWorld(data));

        handler.Execute(connection, new CCharacterCreatePacket
        {
            Name = "Navcheck",
            Class = (int)createInfo.Class,
        });

        await PumpAsync(connection);

        Domain.Characters.CharacterStats createdStats = (Domain.Characters.CharacterStats)stats.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(ICharacterStatsRepository.CreateAsync))
            .GetArguments()[0]!;
        Assert.Null(createdStats.Character);
        Assert.NotEqual(default, createdStats.CharacterId);

        List<ItemInstance> instances = (List<ItemInstance>)items.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IItemInstanceRepository.CreateAsync))
            .GetArguments()[0]!;
        Assert.NotEmpty(instances);
        Assert.All(instances, instance =>
        {
            Assert.NotEqual(Guid.Empty, instance.Id.Value);
            Assert.NotEqual(default, instance.TemplateId);
        });
    }

    /// <summary>
    /// The gender the client picks is the one the character is stored with, and the one
    /// <see cref="ICharacter.Gender"/> reports once that row is loaded into the world. The
    /// dialogue text's <c>{g:masculine|feminine}</c> construct resolves against that property.
    /// </summary>
    [Fact]
    public async Task Persist_the_gender_the_client_picked()
    {
        StaticData data = await LoadStaticDataAsync();
        CharacterCreateInfo createInfo = data.CharacterCreateInfos.First();

        IWorldConnection connection = NewConnection();
        NewHandler(data).Execute(connection, new CCharacterCreatePacket
        {
            Name = "Guerreira",
            Class = (int)createInfo.Class,
            Gender = (int)CharacterGender.Female,
        });

        await PumpAsync(connection);

        Assert.Equal(SCharacterCreateResult.Success, SentResult(connection));

        await using CharacterDbContext characterDb = _characters.CreateDbContext();
        Avalon.Domain.Characters.Character character =
            await characterDb.Characters.AsNoTracking().SingleAsync(c => c.Name == "Guerreira");
        Assert.Equal(CharacterGender.Female, character.Gender);

        ICharacter entity = new CharacterEntity(NullLoggerFactory.Instance, character, new RegenConfiguration());
        Assert.Equal(CharacterGender.Female, entity.Gender);
    }

    /// <summary>
    /// The current client does not send the field at all. Protobuf reads an absent int as 0,
    /// which is <see cref="CharacterGender.Male"/>, so that client keeps creating characters.
    /// </summary>
    [Fact]
    public async Task Create_a_male_character_when_the_client_omits_the_gender()
    {
        StaticData data = await LoadStaticDataAsync();
        CharacterCreateInfo createInfo = data.CharacterCreateInfos.First();

        // Round-trip the legacy shape through the serializer, so the default comes from the
        // wire and not from a C# initializer.
        CCharacterCreatePacket legacy = Serializer.Deserialize<CCharacterCreatePacket>(
            Serialize(new LegacyCharacterCreatePacket { Name = "Guerreiro", Class = (int)createInfo.Class }));

        IWorldConnection connection = NewConnection();
        NewHandler(data).Execute(connection, legacy);

        await PumpAsync(connection);

        Assert.Equal(SCharacterCreateResult.Success, SentResult(connection));

        await using CharacterDbContext characterDb = _characters.CreateDbContext();
        Avalon.Domain.Characters.Character character =
            await characterDb.Characters.AsNoTracking().SingleAsync(c => c.Name == "Guerreiro");
        Assert.Equal(CharacterGender.Male, character.Gender);
    }

    /// <summary>
    /// The value comes from the client, so anything the enum does not define is refused rather
    /// than stored. 256 is there because the enum is a byte: a cast alone would wrap it to Male.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(int.MaxValue)]
    public async Task Reject_a_gender_the_enum_does_not_define(int gender)
    {
        StaticData data = await LoadStaticDataAsync();
        CharacterCreateInfo createInfo = data.CharacterCreateInfos.First();

        IWorldConnection connection = NewConnection();
        NewHandler(data).Execute(connection, new CCharacterCreatePacket
        {
            Name = "Nobody",
            Class = (int)createInfo.Class,
            Gender = gender,
        });

        await PumpAsync(connection);

        Assert.Equal(SCharacterCreateResult.InvalidClass, SentResult(connection));

        await using CharacterDbContext characterDb = _characters.CreateDbContext();
        Assert.False(await characterDb.Characters.AnyAsync());
    }

    private static SCharacterCreateResult SentResult(IWorldConnection connection)
    {
        NetworkPacket sent = (NetworkPacket)connection.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send))
            .GetArguments()[0]!;

        // EchoCryptoSession leaves the payload as serialized.
        return Serializer.Deserialize<SCharacterCreatedPacket>(new MemoryStream(sent.Payload)).Result;
    }

    private static MemoryStream Serialize<T>(T value)
    {
        MemoryStream stream = new();
        Serializer.Serialize(stream, value);
        stream.Position = 0;
        return stream;
    }

    /// <summary>The packet as a client that predates the gender field serializes it.</summary>
    [ProtoContract]
    private sealed class LegacyCharacterCreatePacket
    {
        [ProtoMember(1)] public string Name { get; set; } = string.Empty;
        [ProtoMember(2)] public int Class { get; set; }
    }

    private async Task<StaticData> LoadStaticDataAsync()
    {
        StaticData data = new(
            new CharacterCreateInfoRepository(_world),
            new ClassLevelStatRepository(_world),
            new ItemTemplateRepository(_world),
            new AbilityTemplateRepository(_world),
            new CharacterLevelExperienceRepository(_world),
            new CreatureTemplateRepository(_world),
            new CreatureBaseStatRepository(_world),
            new CreatureRarityModifierRepository(_world),
            new LocalizedTextRepository(_world),
            new DialogueRepository(_world),
            new LootTableRepository(_world),
            NullLoggerFactory.Instance);

        await data.LoadAsync();
        return data;
    }

    private CharacterCreateHandler NewHandler(StaticData data) => new(
        NullLogger<CharacterCreateHandler>.Instance,
        new CharacterRepository(_characters),
        new CharacterStatsRepository(_characters),
        new CharacterAbilityRepository(_characters),
        new CharacterInventoryRepository(_characters),
        new ItemInstanceRepository(_characters),
        new ItemIdAllocator(),
        NewWorld(data));

    private static IWorld NewWorld(StaticData data)
    {
        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration { MaxCharactersPerAccount = 10 });
        world.Data.Returns(data);
        return world;
    }

    private static IWorldConnection NewConnection()
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.AccountId.Returns(new AccountId(1));
        // A substitute hands back a substitute for an interface-typed property, and a non-null
        // Character reads to the handler as "one is already selected".
        connection.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null!);
        connection.CryptoSession.Returns(new EchoCryptoSession());
        return connection;
    }

    /// <summary>
    /// The tick loop's continuation drain, in miniature. Each callback enqueues the next, so this
    /// walks the recorded calls forward rather than draining a snapshot of them.
    /// </summary>
    private static async Task PumpAsync(IWorldConnection connection)
    {
        for (int processed = 0; processed < 64; processed++)
        {
            List<ICall> enqueued = connection.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IWorldConnection.EnqueueContinuation))
                .ToList();

            if (processed >= enqueued.Count)
            {
                return;
            }

            object?[] arguments = enqueued[processed].GetArguments();
            Task task = (Task)arguments[0]!;
            await task;

            Delegate callback = (Delegate)arguments[1]!;
            object?[] callbackArguments = callback.Method.GetParameters().Length == 0
                ? []
                : [task.GetType().GetProperty("Result")!.GetValue(task)];

            callback.DynamicInvoke(callbackArguments);
        }

        throw new InvalidOperationException("The continuation chain did not terminate.");
    }

    public void Dispose()
    {
        _characters.Dispose();
        _world.Dispose();
    }

    private sealed class EchoCryptoSession : IAvalonCryptoSession
    {
        public void Initialize(byte[] otherEndPublicKeyBytes) { }
        public byte[] GetPublicKey() => [];
        public byte[] GetOtherEndPublicKey() => [];
        public byte[] Encrypt(ReadOnlySpan<byte> data) => data.ToArray();
        public int Decrypt(ReadOnlySpan<byte> data, byte[] output) => 0;
        public byte[] GenerateHandshakeData() => [];
    }
}
