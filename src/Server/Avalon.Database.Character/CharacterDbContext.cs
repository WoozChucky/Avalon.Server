using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Avalon.Database.Character;

public class CharacterDbContext : DbContext
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _connectionString;
    
    public DbSet<Domain.Characters.Character> Characters { get; set; } = null!;

    public CharacterDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.Characters!.ConnectionString;
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
        Configure(modelBuilder.Entity<Domain.Characters.Character>());
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
}
