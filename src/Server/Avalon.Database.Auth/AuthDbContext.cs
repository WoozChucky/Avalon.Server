using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OperatingSystem = Avalon.Domain.Auth.OperatingSystem;

namespace Avalon.Database.Auth;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
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
                          ?? configuration["Database:Auth:ConnectionString"]
                          ?? throw new InvalidOperationException(
                              "Auth connection string not found for design time. " +
                              "Provide Database:Auth:ConnectionString in appsettings.Design.json or Database__Auth__ConnectionString env var.");

        // 3) Minimal logger factory (keeps parity with your OnConfiguring)
        ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddConsole();
        });

        // 4) Construct the context using public constructor
        //    context reads opts.Value.Auth.ConnectionString internally.
        IOptions<DatabaseConfiguration> opts = Options.Create(new DatabaseConfiguration
        {
            Auth = new DatabaseConnection {ConnectionString = authConn}
        });

        AuthDbContext ctx = new(loggerFactory, opts);

        // 5) Mirror OnConfiguring behavior
        //    Left here just to highlight parity
        //    ctx.Database.SetCommandTimeout(TimeSpan.FromSeconds(60)); // example tweak if you want

        return ctx;
    }
}

public class AuthDbContext : DbContext
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly string? _connectionString;

    public AuthDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.Auth!.ConnectionString;
    }

    /// <summary>Configured by the caller. Lets a test point the same model at another provider.</summary>
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<Device> Devices { get; set; } = null!;
    public DbSet<MFASetup> MfaSetups { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<AvalonToken> Tokens { get; set; } = null!;
    public DbSet<PersonalAccessToken> PersonalAccessTokens { get; set; } = null!;
    public DbSet<Domain.Auth.World> Worlds { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        optionsBuilder
            .UseLoggerFactory(_loggerFactory)
            .EnableSensitiveDataLogging();

        optionsBuilder.UseNpgsql(_connectionString!);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<Account>());
        Configure(modelBuilder.Entity<Device>());
        Configure(modelBuilder.Entity<MFASetup>());
        Configure(modelBuilder.Entity<RefreshToken>());
        Configure(modelBuilder.Entity<AvalonToken>());
        Configure(modelBuilder.Entity<PersonalAccessToken>());
        Configure(modelBuilder.Entity<Domain.Auth.World>());
    }

    /// <summary>The check constraint that holds <c>Accounts.Username</c> trimmed and upper-cased (#487).</summary>
    public const string UsernameNormalisedConstraint = "CK_Accounts_Username_Normalised";

    /// <summary>The check constraint that holds <c>Accounts.Email</c> trimmed and lower-cased (#503).</summary>
    public const string EmailNormalisedConstraint = "CK_Accounts_Email_Normalised";

    private static void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            ).IsRequired();

        // One account per username (#487). Usernames are stored the way they are looked up,
        // trimmed and upper-cased (registration normalises them), so an index on the column is an
        // index on the normalised name. Without it two registrations racing past the "taken" check
        // both inserted, and which account a login reached depended on the row order.
        builder.HasIndex(b => b.Username).IsUnique();

        // ...and the database holds every writer to that form (#487 review), so the index cannot
        // stop being on the normalised name. upper() and trim() are standard SQL, so the same
        // expression runs on Postgres and on the SQLite the tests use.
        builder.ToTable(t => t.HasCheckConstraint(UsernameNormalisedConstraint,
            "\"Username\" = upper(trim(\"Username\"))"));

        // One account per email (#503), on the form every writer stores and every lookup uses:
        // trimmed and lower-cased (AccountEmail.Normalise). Before it, the lookup was exact and the
        // email stored as sent, so A@x.com and a@x.com could be two accounts. The check constraint
        // holds every writer to that form, as for the username.
        builder.HasIndex(b => b.Email).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint(EmailNormalisedConstraint,
            "\"Email\" = lower(trim(\"Email\"))"));

        builder.Property(b => b.Locale)
            .HasConversion(new EnumToStringConverter<AccountLocale>());
        builder.Property(b => b.Os)
            .HasConversion(new EnumToStringConverter<OperatingSystem>());

        builder.HasData(new Account
        {
            Id = 1,
            Username = "ADMIN",
            Salt =
                new byte[]
                {
                    36, 50, 97, 36, 49, 49, 36, 87, 99, 81, 111, 50, 73, 79, 51, 110, 69, 119, 75, 77, 78, 85, 98,
                    116, 110, 71, 88, 90, 46
                },
            Verifier =
                new byte[] // 123
                {
                    36, 50, 97, 36, 49, 49, 36, 87, 99, 81, 111, 50, 73, 79, 51, 110, 69, 119, 75, 77, 78, 85, 98,
                    116, 110, 71, 88, 90, 46, 54, 72, 106, 115, 116, 79, 46, 107, 82, 120, 110, 46, 80, 115, 80, 83,
                    85, 98, 55, 47, 70, 103, 116, 50, 69, 97, 119, 107, 53, 105, 54
                },
            SessionKey = [],
            Email = "admin@avalon.monster",
            JoinDate = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastIp = "127.0.0.1",
            LastAttemptIp = string.Empty,
            FailedLogins = 0,
            Locked = false,
            Locale = AccountLocale.ptPT,
            Os = OperatingSystem.Linux,
            TotalTime = 0,
            AccessLevel =
                AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin,
            LastLogin = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Online = false,
            MuteBy = string.Empty,
            MuteReason = string.Empty,
            MuteTime = null
        });
    }

    private static void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.AccountId)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            ).IsRequired();

        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<MFASetup> builder)
    {
        builder.HasKey(b => b.Id);

        // One MFA row per account (#470). With two, login read whichever came back first, and a
        // row still in Setup let a password-only login through an enrolled account.
        builder.HasIndex(b => b.AccountId).IsUnique();

        builder.Property(b => b.AccountId)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            ).IsRequired();

        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.AccountId)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            ).IsRequired();

        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.AccountId, r.FamilyId });
        builder.HasIndex(r => r.Hash).IsUnique();
    }

    private static void Configure(EntityTypeBuilder<AvalonToken> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.AccountId)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            ).IsRequired();

        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<PersonalAccessToken> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new PersonalAccessTokenId(v)
            ).IsRequired();

        builder.Property(b => b.AccountId)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            ).IsRequired();

        builder.Property(b => b.RevokedBy)
            .HasConversion(
                v => v!.Value,
                v => new AccountId(v));

        builder.HasIndex(b => b.TokenHash).IsUnique();
        builder.HasIndex(b => new { b.AccountId, b.RevokedAt });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<Domain.Auth.World> builder)
    {
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new WorldId(v)
            ).IsRequired();

        builder.HasData(new Domain.Auth.World
        {
            Id = 1,
            Host = "127.0.0.1",
            Port = 21001,
            Name = "Development",
            Status = WorldStatus.Online,
            Type = WorldType.PvE,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            AccessLevelRequired = AccountAccessLevel.Admin,
            CreatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }, new Domain.Auth.World
        {
            Id = 2,
            Host = "asthoria.avalon.monster",
            Port = 21001,
            Name = "Asthoria",
            Status = WorldStatus.Offline,
            Type = WorldType.PvE,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            AccessLevelRequired = AccountAccessLevel.Player,
            CreatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }, new Domain.Auth.World
        {
            Id = 3,
            Host = "ptr.avalon.monster",
            Port = 21001,
            Name = "Public Test Realm",
            Status = WorldStatus.Offline,
            Type = WorldType.PvE,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            AccessLevelRequired = AccountAccessLevel.PTR,
            CreatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
