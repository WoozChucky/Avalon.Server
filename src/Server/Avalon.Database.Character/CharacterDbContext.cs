using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Database.Character;

public sealed class CharacterDbContextFactory : IDesignTimeDbContextFactory<CharacterDbContext>
{
    public CharacterDbContext CreateDbContext(string[] args)
    {
        // 1) Load a deterministic, design-time configuration
        //    Priority: appsettings.Design.json (repo-local), then environment variables, then appsettings.json if present.
        string basePath = Directory.GetCurrentDirectory(); // root where you run dotnet ef
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.Design.json", true)
            .AddJsonFile("appsettings.json", true) // optional convenience
            .AddEnvironmentVariables()
            .Build();

        // 2) Resolve strongly-typed configuration the same way runtime does
        DatabaseConfiguration dbConfig = new();
        configuration.GetSection("Database").Bind(dbConfig);

        string authConn = dbConfig.Auth?.ConnectionString
                          ?? configuration["Database:Characters:ConnectionString"]
                          ?? throw new InvalidOperationException(
                              "Characters connection string not found for design time. " +
                              "Provide Database:Characters:ConnectionString in appsettings.Design.json or Database__Characters__ConnectionString env var.");

        // 3) Minimal logger factory (keeps parity with your OnConfiguring)
        ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddConsole();
        });

        // 4) Construct the context using public constructor
        //    context reads opts.Value.Characters.ConnectionString internally.
        IOptions<DatabaseConfiguration> opts = Options.Create(new DatabaseConfiguration
        {
            Characters = new DatabaseConnection {ConnectionString = authConn}
        });

        CharacterDbContext ctx = new(loggerFactory, opts);

        // 5) Mirror OnConfiguring behavior
        //    Left here just to highlight parity
        //    ctx.Database.SetCommandTimeout(TimeSpan.FromSeconds(60)); // example tweak if you want

        return ctx;
    }
}

public class CharacterDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    : DbContext
{
    private readonly string _connectionString = opts.Value.Characters!.ConnectionString;

    public DbSet<Domain.Characters.Character> Characters { get; set; } = null!;
    public DbSet<CharacterStats> CharacterStats { get; set; } = null!;
    public DbSet<CharacterInventory> CharacterInventory { get; set; } = null!;
    public DbSet<CharacterSpell> CharacterSpells { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseLoggerFactory(loggerFactory)
            .EnableSensitiveDataLogging();

        optionsBuilder.UseNpgsql(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<Domain.Characters.Character>());
        Configure(modelBuilder.Entity<CharacterStats>());
        Configure(modelBuilder.Entity<CharacterInventory>());
        Configure(modelBuilder.Entity<CharacterSpell>());
    }

    private static void Configure(EntityTypeBuilder<Domain.Characters.Character> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            )
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(b => b.AccountId)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            );
    }

    private static void Configure(EntityTypeBuilder<CharacterStats> builder)
    {
        builder.HasKey(b => b.CharacterId);

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            ).IsRequired();

        builder.HasOne(e => e.Character)
            .WithOne()
            .HasForeignKey<CharacterStats>(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.CharacterId);
    }

    private static void Configure(EntityTypeBuilder<CharacterInventory> builder)
    {
        builder.HasKey(b => new {b.CharacterId, b.Container, b.Slot});

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            ).IsRequired();

        builder.Property(b => b.ItemId)
            .HasConversion(
                v => v.Value,
                v => new ItemInstanceId(v)
            );

        builder.HasOne(e => e.Character)
            .WithMany()
            .HasForeignKey(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.CharacterId);
    }

    private static void Configure(EntityTypeBuilder<CharacterSpell> builder)
    {
        builder.HasKey(b => new {b.CharacterId, b.SpellId});

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            ).IsRequired();

        builder.Property(b => b.SpellId)
            .HasConversion(
                v => v.Value,
                v => new SpellId(v)
            );

        builder.HasOne(e => e.Character)
            .WithMany()
            .HasForeignKey(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.CharacterId);
    }
}
