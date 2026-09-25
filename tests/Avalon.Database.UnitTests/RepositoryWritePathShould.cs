using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// A repository write covers one row. These pin both halves of it against a real database: the
/// write a caller is meant to make lands, and a graph is refused rather than guessed at.
/// </summary>
public class RepositoryWritePathShould
{
    [Fact]
    public async Task Insert_a_dependent_that_names_its_principal_by_foreign_key()
    {
        using SqliteDatabase<Auth.AuthDbContext> database = SqliteDatabase.Auth();
        AccountRepository accounts = new(database);
        DeviceRepository devices = new(database);

        Account account = await accounts.CreateAsync(NewAccount());

        await devices.CreateAsync(new Device
        {
            AccountId = account.Id,
            Name = "test-agent",
            LastUsage = DateTime.UtcNow,
            Trusted = false,
            TrustEnd = DateTime.UtcNow,
        });

        await using Auth.AuthDbContext context = database.CreateDbContext();
        Assert.Equal(1, await context.Accounts.CountAsync(a => a.Id == account.Id));
        Assert.Equal(1, await context.Devices.CountAsync(d => d.AccountId == account.Id));
    }

    /// <summary>
    /// The account came back from a context that is already disposed, so it is detached. Add would
    /// mark it Added along with the device and insert the row a second time.
    /// </summary>
    [Fact]
    public async Task Refuse_a_dependent_that_names_its_principal_by_navigation()
    {
        using SqliteDatabase<Auth.AuthDbContext> database = SqliteDatabase.Auth();
        AccountRepository accounts = new(database);
        DeviceRepository devices = new(database);

        Account account = await accounts.CreateAsync(NewAccount());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => devices.CreateAsync(new Device
            {
                Account = account,
                AccountId = account.Id,
                Name = "test-agent",
                LastUsage = DateTime.UtcNow,
                Trusted = false,
                TrustEnd = DateTime.UtcNow,
            }));

        Assert.Contains(nameof(Device), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Account), error.Message, StringComparison.Ordinal);

        await using Auth.AuthDbContext context = database.CreateDbContext();
        Assert.Equal(1, await context.Accounts.CountAsync(a => a.Id == account.Id));
        Assert.Equal(0, await context.Devices.CountAsync());
    }

    /// <summary>
    /// The child is new and its key is client-assigned, so no reading of the key can tell it apart
    /// from a row that exists. Inserting the pool and dropping the membership is what a guess does,
    /// and it does it while returning success.
    /// </summary>
    [Fact]
    public async Task Refuse_a_parent_that_carries_a_new_child()
    {
        using SqliteDatabase<World.WorldDbContext> database = SqliteDatabase.World();
        ChunkPoolRepository pools = new(database);

        ChunkTemplate template;
        await using (World.WorldDbContext read = database.CreateDbContext())
        {
            template = new ChunkTemplate { Id = new ChunkTemplateId(4242), Name = "probe" };
            read.ChunkTemplates.Add(template);
            await read.SaveChangesAsync();
        }

        ChunkPool pool = new()
        {
            Id = new ChunkPoolId(99),
            Name = "probe-pool",
            Memberships =
            [
                new ChunkPoolMembership
                {
                    ChunkPoolId = new ChunkPoolId(99),
                    ChunkTemplateId = template.Id,
                    Weight = 1f,
                },
            ],
        };

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => pools.CreateAsync(pool));

        Assert.Contains(nameof(ChunkPool), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ChunkPoolMembership), error.Message, StringComparison.Ordinal);

        await using World.WorldDbContext context = database.CreateDbContext();
        Assert.Equal(0, await context.ChunkPools.CountAsync());
    }

    /// <summary>
    /// An owned collection is part of the owner's row. There is no foreign key a caller could name
    /// it by, so the rule does not reach it.
    /// </summary>
    [Fact]
    public async Task Insert_an_owned_collection_with_its_owner()
    {
        using SqliteDatabase<World.WorldDbContext> database = SqliteDatabase.World();
        ChunkTemplateRepository templates = new(database);

        await templates.CreateAsync(new ChunkTemplate
        {
            Id = new ChunkTemplateId(7777),
            Name = "owned-probe",
            SpawnSlots = [new ChunkSpawnSlot { Tag = "entry" }, new ChunkSpawnSlot { Tag = "pack" }],
        });

        await using World.WorldDbContext context = database.CreateDbContext();
        ChunkTemplate stored = await context.ChunkTemplates
            .AsNoTracking()
            .SingleAsync(t => t.Id == new ChunkTemplateId(7777));

        Assert.Equal(2, stored.SpawnSlots.Count);
    }

    [Fact]
    public async Task Insert_a_list_of_dependents_that_name_their_principal_by_foreign_key()
    {
        using SqliteDatabase<Character.CharacterDbContext> database = SqliteDatabase.Characters();
        Character.Repositories.CharacterRepository characters = new(database);
        Character.Repositories.ItemInstanceRepository items = new(database);
        Character.Repositories.CharacterInventoryRepository slots = new(database);

        Domain.Characters.Character owner = await characters.CreateAsync(new Domain.Characters.Character
        {
            AccountId = new AccountId(1), Name = "Holder", CreationDate = DateTime.UtcNow,
        });

        List<ItemInstance> created = await items.CreateAsync([NewItemInstance(owner.Id), NewItemInstance(owner.Id)]);

        await slots.CreateAsync(created
            .Select((item, index) => new Domain.Characters.CharacterInventory
            {
                CharacterId = owner.Id, Container = InventoryType.Bag, Slot = (ushort)index, ItemId = item.Id,
            })
            .ToList());

        await using Character.CharacterDbContext context = database.CreateDbContext();
        Assert.Equal(2, await context.ItemInstances.CountAsync(i => i.CharacterId == owner.Id));
        Assert.Equal(2, await context.CharacterInventory.CountAsync(s => s.CharacterId == owner.Id));
    }

    /// <summary>
    /// The update half of the rule, symmetric with the insert half. Marking the principal Modified
    /// would write a row the caller never asked to write.
    /// </summary>
    [Fact]
    public async Task Refuse_an_update_whose_entity_names_its_principal_by_navigation()
    {
        using SqliteDatabase<Auth.AuthDbContext> database = SqliteDatabase.Auth();
        AccountRepository accounts = new(database);
        DeviceRepository devices = new(database);

        Account account = await accounts.CreateAsync(NewAccount());
        Device device = await devices.CreateAsync(new Device
        {
            AccountId = account.Id,
            Name = "test-agent",
            LastUsage = DateTime.UtcNow,
            Trusted = false,
            TrustEnd = DateTime.UtcNow,
        });

        device.Account = account;
        device.Trusted = true;

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => devices.UpdateAsync(device));

        Assert.Contains(nameof(Device), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Account), error.Message, StringComparison.Ordinal);

        await using Auth.AuthDbContext context = database.CreateDbContext();
        Assert.False((await context.Devices.SingleAsync()).Trusted);
    }

    /// <summary>
    /// KNOWN LIMITATION, pinned as it is rather than as it should be: an owner that carries owned
    /// collections cannot currently be updated through a repository at all. The owned nodes are
    /// exempt from the refusal and take the root's Modified state, EF rejects re-tracking them, and
    /// the update does not land — even when nothing about the owned rows changed.
    ///
    /// This is preferred to what it replaced. Under the previous key test the same update succeeded
    /// and duplicated the owned rows, two slots becoming four, with no error. A loud failure on a
    /// path nothing calls beats silent corruption on it. Fixing it is its own change; an early
    /// return on owned nodes is not the fix, because that drops them.
    /// </summary>
    [Fact]
    public async Task Fail_loudly_when_an_owner_with_owned_rows_is_updated()
    {
        using SqliteDatabase<World.WorldDbContext> database = SqliteDatabase.World();
        ChunkTemplateRepository templates = new(database);
        ChunkTemplateId id = new(8888);

        await templates.CreateAsync(new ChunkTemplate
        {
            Id = id,
            Name = "before",
            SpawnSlots = [new ChunkSpawnSlot { Tag = "entry" }, new ChunkSpawnSlot { Tag = "pack" }],
        });

        ChunkTemplate stored = (await templates.FindByIdAsync(id))!;
        stored.Name = "after";

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => templates.UpdateAsync(stored));

        // EF's own refusal, not ours: the owned rows are exempt and reach the change tracker.
        Assert.Contains(nameof(ChunkSpawnSlot), error.Message, StringComparison.Ordinal);
        Assert.Contains("already being tracked", error.Message, StringComparison.Ordinal);

        await using World.WorldDbContext context = database.CreateDbContext();
        ChunkTemplate unchanged = await context.ChunkTemplates.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal("before", unchanged.Name);
        Assert.Equal(2, unchanged.SpawnSlots.Count);
    }

    private static ItemInstance NewItemInstance(CharacterId owner) => new()
    {
        Id = new ItemInstanceId(Guid.CreateVersion7()),
        TemplateId = new ItemTemplateId(1),
        CharacterId = owner,
        Count = 1,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Account NewAccount() => new()
    {
        Username = "WRITEPATH",
        Email = "writepath@avalon.monster",
        Salt = [1, 2, 3],
        Verifier = [4, 5, 6],
        SessionKey = [],
        LastIp = "127.0.0.1",
        LastAttemptIp = string.Empty,
        MuteBy = string.Empty,
        MuteReason = string.Empty,
        JoinDate = DateTime.UtcNow,
        LastLogin = DateTime.UtcNow,
    };
}
