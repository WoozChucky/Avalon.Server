using System.Diagnostics;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Extensions;
using Avalon.Database.Character;
using Avalon.Database.Character.Extensions;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Benchmarking.Database;

/// <summary>Fresh production contexts and warm query translation; never opens a database connection.</summary>
[MemoryDiagnoser]
public class DbContextFactoryBenchmarks
{
    private ServiceProvider _provider = null!;
    private IDbContextFactory<AuthDbContext> _auth = null!;
    private IDbContextFactory<CharacterDbContext> _characters = null!;

    [Params("Auth", "Characters")]
    public string Database { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string Connection = "Host=127.0.0.1;Port=1;Database=benchmark_no_connection;Username=none;Password=none";
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Connection,
            ["Database:Characters:ConnectionString"] = Connection,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddAuthDatabase();
        services.AddCharacterDatabase();
        _provider = services.BuildServiceProvider();
        _auth = _provider.GetRequiredService<IDbContextFactory<AuthDbContext>>();
        _characters = _provider.GetRequiredService<IDbContextFactory<CharacterDbContext>>();

        // Short warmups can leave tiered PGO changing this path during measurement.
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(30))
        {
            for (int i = 0; i < 128; i++)
                CreateAndTranslateQuery();
        }
    }

    [Benchmark]
    public int CreateAndTranslateQuery()
    {
        if (Database == "Auth")
        {
            using AuthDbContext context = _auth.CreateDbContext();
            return context.Accounts.AsNoTracking().Take(1).ToQueryString().Length;
        }

        using CharacterDbContext characters = _characters.CreateDbContext();
        return characters.Characters.AsNoTracking().Take(1).ToQueryString().Length;
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();
}
