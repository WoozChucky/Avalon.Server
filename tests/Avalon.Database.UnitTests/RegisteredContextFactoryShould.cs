using Avalon.Database.Auth;
using Avalon.Database.Auth.Extensions;
using Avalon.Database.Character;
using Avalon.Database.Character.Extensions;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Database.UnitTests;

public class RegisteredContextFactoryShould
{
    [Theory]
    [InlineData("Auth")]
    [InlineData("Characters")]
    public async Task Keep_context_state_and_connections_isolated_between_calls_and_hosts(string database)
    {
        using ServiceProvider firstHost = BuildHost("first");
        using ServiceProvider secondHost = BuildHost("second");
        using DbContext first = await CreateAsync(firstHost, database);
        using DbContext sibling = await CreateAsync(firstHost, database);
        using DbContext otherHost = await CreateAsync(secondHost, database);

        object entity = database == "Auth"
            ? new Account { Username = "test", Salt = [], Verifier = [], Email = null, JoinDate = DateTime.UnixEpoch }
            : new Domain.Characters.Character { Name = "Test" };
        first.Add(entity);
        Assert.Single(first.ChangeTracker.Entries());
        Assert.Empty(sibling.ChangeTracker.Entries());
        Assert.Empty(otherHost.ChangeTracker.Entries());
        Assert.Equal($"first_{database}", first.Database.GetDbConnection().Database);
        Assert.Equal($"first_{database}", sibling.Database.GetDbConnection().Database);
        Assert.Equal($"second_{database}", otherHost.Database.GetDbConnection().Database);
        Assert.NotSame(first.Database.GetDbConnection(), sibling.Database.GetDbConnection());

        // Disposing one call must not poison another call or the factory's next context.
        first.Dispose();
        using DbContext next = await CreateAsync(firstHost, database);
        Assert.Empty(sibling.ChangeTracker.Entries());
        Assert.Empty(next.ChangeTracker.Entries());
        Assert.Equal($"first_{database}", next.Database.GetDbConnection().Database);
    }

    private static ServiceProvider BuildHost(string name)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = $"Host=127.0.0.1;Port=1;Database={name}_Auth",
            ["Database:Characters:ConnectionString"] = $"Host=127.0.0.1;Port=1;Database={name}_Characters",
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddAuthDatabase();
        services.AddCharacterDatabase();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<DbContext> CreateAsync(ServiceProvider provider, string database) => database == "Auth"
        ? await provider.GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContextAsync()
        : await provider.GetRequiredService<IDbContextFactory<CharacterDbContext>>().CreateDbContextAsync();
}
