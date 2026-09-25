using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
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

        // The item instances name their template by foreign key. A navigation would point at the
        // cached template StaticData has held since startup, and insert it a second time.
        await using WorldDbContext worldDb = _world.CreateDbContext();
        Assert.Equal(createInfo.StartingItems.Count,
            await worldDb.ItemInstances.CountAsync(i => i.CharacterId == character.Id));
        Assert.Equal(createInfo.StartingItems.Distinct().Count(),
            await worldDb.ItemTemplates.CountAsync(t => createInfo.StartingItems.Contains(t.Id)));
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
            characters, stats, abilities, inventory, items, NewWorld(data));

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
            Assert.Null(instance.Template);
            Assert.NotEqual(default, instance.TemplateId);
        });
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
        new ItemInstanceRepository(_world),
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
