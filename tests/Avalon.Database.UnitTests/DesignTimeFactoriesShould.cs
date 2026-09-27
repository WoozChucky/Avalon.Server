using System.Reflection;
using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// The World and Character design-time factories (#523) read Database:World / Database:Characters
/// from the environment or the database project's user-secrets only, never from an appsettings file
/// in the working directory (the api's lists Database:Worlds now), and refuse without the string.
/// </summary>
public class DesignTimeFactoriesShould
{
    internal const string Probe = "Host=127.0.0.1;Port=1;Database=design_time_probe";

    public static TheoryData<string> DesignTimeAssemblies => new() { "World", "Characters" };

    private static Assembly AssemblyOf(string database) => database switch
    {
        "World" => typeof(WorldDbContext).Assembly,
        _ => typeof(CharacterDbContext).Assembly,
    };

    private static IConfiguration With(string key, string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value })
            .Build();

    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    [Fact]
    public void Refuse_the_world_context_without_its_connection_string()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new WorldDbContextDesignTimeFactory().CreateDbContext(Empty()));

        Assert.Equal("set Database__World__ConnectionString (or user-secrets) to run dotnet ef against a database",
            ex.Message);
    }

    [Fact]
    public void Refuse_the_characters_context_without_its_connection_string()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new CharacterDbContextDesignTimeFactory().CreateDbContext(Empty()));

        Assert.Equal("set Database__Characters__ConnectionString (or user-secrets) to run dotnet ef against a database",
            ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuse_a_blank_world_connection_string(string blank) =>
        Assert.Throws<InvalidOperationException>(() =>
            new WorldDbContextDesignTimeFactory().CreateDbContext(With("Database:World:ConnectionString", blank)));

    [Fact]
    public void Not_take_the_characters_string_for_the_world_context() =>
        Assert.Throws<InvalidOperationException>(() =>
            new WorldDbContextDesignTimeFactory().CreateDbContext(With("Database:Characters:ConnectionString", Probe)));

    [Fact]
    public void Use_the_world_connection_string_it_is_given()
    {
        using WorldDbContext context = new WorldDbContextDesignTimeFactory()
            .CreateDbContext(With("Database:World:ConnectionString", Probe));

        Assert.Equal(Probe, context.Database.GetConnectionString());
    }

    [Fact]
    public void Use_the_characters_connection_string_it_is_given()
    {
        using CharacterDbContext context = new CharacterDbContextDesignTimeFactory()
            .CreateDbContext(With("Database:Characters:ConnectionString", Probe));

        Assert.Equal(Probe, context.Database.GetConnectionString());
    }

    /// <summary>Find is Require without the refusal, for a caller that skips instead (#557).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_nothing_for_a_missing_or_blank_connection_string(string? value) =>
        Assert.Null(DesignTimeConnectionString.Find(With("Database:World:ConnectionString", value), "World"));

    [Fact]
    public void Find_the_connection_string_it_is_given() =>
        Assert.Equal(Probe, DesignTimeConnectionString.Find(With("Database:World:ConnectionString", Probe), "World"));

    [Theory]
    [MemberData(nameof(DesignTimeAssemblies))]
    public void Read_no_appsettings_file(string database)
    {
        var root = (IConfigurationRoot)DesignTimeConnectionString.Sources(AssemblyOf(database));

        // Exactly two sources: the project's user-secrets (its secrets.json, added only when the
        // project declares a UserSecretsId) and the environment. No appsettings file of any name.
        Assert.Equal(2, root.Providers.Count());
        JsonConfigurationProvider secrets = Assert.Single(root.Providers.OfType<JsonConfigurationProvider>());
        Assert.Equal("secrets.json", Path.GetFileName(secrets.Source.Path));
        Assert.Single(root.Providers.OfType<EnvironmentVariablesConfigurationProvider>());
    }
}

[CollectionDefinition(nameof(ProcessEnvironment), DisableParallelization = true)]
public sealed class ProcessEnvironment
{
}

/// <summary>Sets real process variables, so it runs alone.</summary>
[Collection(nameof(ProcessEnvironment))]
public class DesignTimeFactoryEnvironmentShould
{
    private static void WithVariable(string name, string value, Action body)
    {
        string? before = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try { body(); }
        finally { Environment.SetEnvironmentVariable(name, before); }
    }

    [Fact]
    public void Read_the_world_connection_string_from_the_environment() =>
        WithVariable("Database__World__ConnectionString", DesignTimeFactoriesShould.Probe, () =>
        {
            using WorldDbContext context = new WorldDbContextDesignTimeFactory().CreateDbContext([]);
            Assert.Equal(DesignTimeFactoriesShould.Probe, context.Database.GetConnectionString());
        });

    [Fact]
    public void Read_the_characters_connection_string_from_the_environment() =>
        WithVariable("Database__Characters__ConnectionString", DesignTimeFactoriesShould.Probe, () =>
        {
            using CharacterDbContext context = new CharacterDbContextDesignTimeFactory().CreateDbContext([]);
            Assert.Equal(DesignTimeFactoriesShould.Probe, context.Database.GetConnectionString());
        });
}
