using System.Text;
using Avalon.Configuration;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Avalon.Auth.Database;

public class AuthDbContext : DbContext
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _connectionString;
    
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<Device> Devices { get; set; } = null!;
    public DbSet<MFASetup> MfaSetups { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<AvalonToken> Tokens { get; set; } = null!;
    public DbSet<World> Worlds { get; set; } = null!;

    public AuthDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.Auth!.ConnectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseLoggerFactory(_loggerFactory);
        
        optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString), mysql =>
        {
            // because MySQL does not support schemas: https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/1100
            mysql.SchemaBehavior(MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}.{table}");
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<Account>(), Database);
        Configure(modelBuilder.Entity<World>(), Database);
    }
    
    private static void Configure(EntityTypeBuilder<Account> builder, DatabaseFacade database)
    {
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        //builder.Property(x => x.CreatedAt).HasDefaultValueSql("(CAST(CURRENT_TIMESTAMP AS DATETIME(6)))");
        //builder.Property(x => x.UpdatedAt).HasDefaultValueSql("(CAST(CURRENT_TIMESTAMP AS DATETIME(6)))");

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
                JoinDate = DateTime.UtcNow,
                LastIp = "127.0.0.1",
                LastAttemptIp = string.Empty,
                FailedLogins = 0,
                Locked = false,
                Locale = "en",
                OS = "WIN",
                TotalTime = 0,
                AccessLevel = AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Administrator,
                LastLogin = DateTime.UtcNow,
                Online = false,
                MuteBy = string.Empty,
                MuteReason = string.Empty,
                MuteTime = null
            }
        ]);
    }

    private static void Configure(EntityTypeBuilder<World> builder, DatabaseFacade database)
    {
        builder.HasData([
            new World
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new World
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new World
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
        ]);
    }
}
