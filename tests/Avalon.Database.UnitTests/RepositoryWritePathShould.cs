using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Database.World.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public class RepositoryWritePathShould
{
    [Fact]
    public async Task Insert_a_dependent_that_points_at_an_already_inserted_principal()
    {
        using SqliteDatabase<Avalon.Database.Auth.AuthDbContext> database = SqliteDatabase.Auth();
        AccountRepository accounts = new(database);
        DeviceRepository devices = new(database);

        Account account = await accounts.CreateAsync(NewAccount());

        // The account came back from a context that is already disposed, so it is detached. Add
        // walks the navigation graph and marks every detached node Added, which would insert the
        // account a second time.
        await devices.CreateAsync(new Device
        {
            Account = account,
            AccountId = account.Id,
            Name = "test-agent",
            LastUsage = DateTime.UtcNow,
            Trusted = false,
            TrustEnd = DateTime.UtcNow,
        });

        await using Avalon.Database.Auth.AuthDbContext context = database.CreateDbContext();
        Assert.Equal(1, await context.Accounts.CountAsync(a => a.Id == account.Id));
        Assert.Equal(1, await context.Devices.CountAsync(d => d.AccountId == account.Id));
    }

    [Fact]
    public async Task Insert_a_list_of_dependents_that_point_at_an_already_inserted_principal()
    {
        using SqliteDatabase<Avalon.Database.World.WorldDbContext> database = SqliteDatabase.World();
        ItemInstanceRepository instances = new(database);

        await using (Avalon.Database.World.WorldDbContext seeded = database.CreateDbContext())
        {
            Assert.True(await seeded.ItemTemplates.AnyAsync());
        }

        // A template out of a cache that has been held since startup: detached, and its row exists.
        ItemTemplate template;
        await using (Avalon.Database.World.WorldDbContext read = database.CreateDbContext())
        {
            template = await read.ItemTemplates.AsNoTracking().FirstAsync();
        }

        await instances.CreateAsync(
        [
            new ItemInstance
            {
                Template = template,
                TemplateId = template.Id,
                CharacterId = new CharacterId(1),
                Count = 1,
                UpdatedAt = DateTime.UtcNow,
            },
            new ItemInstance
            {
                Template = template,
                TemplateId = template.Id,
                CharacterId = new CharacterId(1),
                Count = 1,
                UpdatedAt = DateTime.UtcNow,
            },
        ]);

        await using Avalon.Database.World.WorldDbContext context = database.CreateDbContext();
        Assert.Equal(1, await context.ItemTemplates.CountAsync(t => t.Id == template.Id));
        Assert.Equal(2, await context.ItemInstances.CountAsync(i => i.TemplateId == template.Id));
    }

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
