using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
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
        using SqliteDatabase<World.WorldDbContext> database = SqliteDatabase.World();
        ItemInstanceRepository instances = new(database);

        ItemTemplateId templateId;
        await using (World.WorldDbContext read = database.CreateDbContext())
        {
            templateId = (await read.ItemTemplates.AsNoTracking().FirstAsync()).Id;
        }

        await instances.CreateAsync(
        [
            NewItemInstance(templateId),
            NewItemInstance(templateId),
        ]);

        await using World.WorldDbContext context = database.CreateDbContext();
        Assert.Equal(1, await context.ItemTemplates.CountAsync(t => t.Id == templateId));
        Assert.Equal(2, await context.ItemInstances.CountAsync(i => i.TemplateId == templateId));
    }

    private static ItemInstance NewItemInstance(ItemTemplateId templateId) => new()
    {
        TemplateId = templateId,
        CharacterId = new CharacterId(1),
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
