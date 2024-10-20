using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using OperatingSystem = Avalon.Domain.Auth.OperatingSystem;

namespace Avalon.Database.Auth;

public class AuthDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    : DbContext
{
    private readonly string _connectionString = opts.Value.Auth!.ConnectionString;

    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<Device> Devices { get; set; } = null!;
    public DbSet<MFASetup> MfaSetups { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<AvalonToken> Tokens { get; set; } = null!;
    public DbSet<Domain.Auth.World> Worlds { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseLoggerFactory(loggerFactory);

        optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString), mysql =>
        {
            // because MySQL does not support schemas: https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/1100
            mysql.SchemaBehavior(MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}.{table}");
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<Account>());
        Configure(modelBuilder.Entity<Device>());
        Configure(modelBuilder.Entity<MFASetup>());
        Configure(modelBuilder.Entity<RefreshToken>());
        Configure(modelBuilder.Entity<AvalonToken>());
        Configure(modelBuilder.Entity<Domain.Auth.World>());
    }

    private static void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            ).IsRequired();

        builder.Property(b => b.Locale)
            .HasConversion(new EnumToStringConverter<AccountLocale>());
        builder.Property(b => b.Os)
            .HasConversion(new EnumToStringConverter<OperatingSystem>());

        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword("123", salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);

        builder.HasData([
            new Account
            {
                Id = 1,
                Username = "ADMIN",
                Salt = saltBytes,
                Verifier = hashBytes,
                SessionKey = [],
                Email = "admin@avalon.monster",
                JoinDate = new DateTime(2021, 1, 1),
                LastIp = "127.0.0.1",
                LastAttemptIp = string.Empty,
                FailedLogins = 0,
                Locked = false,
                Locale = AccountLocale.ptPT,
                Os = OperatingSystem.Linux,
                TotalTime = 0,
                AccessLevel = AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Administrator,
                LastLogin = new DateTime(2021, 1, 1),
                Online = false,
                MuteBy = string.Empty,
                MuteReason = string.Empty,
                MuteTime = null
            }
        ]);
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

    private static void Configure(EntityTypeBuilder<Domain.Auth.World> builder)
    {
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new WorldId(v)
            ).IsRequired();

        builder.HasData([
            new Domain.Auth.World
            {
                Id = 1,
                Host = "127.0.0.1",
                Port = 21001,
                Name = "Development",
                Status = WorldStatus.Online,
                Type = WorldType.PvE,
                MinVersion = "0.0.1",
                Version = "0.0.1",
                AccessLevelRequired = AccountAccessLevel.Administrator,
                CreatedAt = new DateTime(2021, 1, 1),
                UpdatedAt = new DateTime(2021, 1, 1)
            },
            new Domain.Auth.World
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
                CreatedAt = new DateTime(2021, 1, 1),
                UpdatedAt = new DateTime(2021, 1, 1)
            },
            new Domain.Auth.World
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
                CreatedAt = new DateTime(2021, 1, 1),
                UpdatedAt = new DateTime(2021, 1, 1)
            },
        ]);
    }
}
