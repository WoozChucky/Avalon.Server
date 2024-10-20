using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Configuration;
using Avalon.Database.Migrator.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Database.Migrator;

public class DatabaseMigrator : IDatabaseMigrator
{
    private readonly ILogger<DatabaseMigrator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MigratorConfiguration _migratorConfiguration;
    private readonly DatabaseConfiguration _databaseConfiguration;
    private readonly ICollection<DatabaseMigrationProcess> _databaseMigrationProcesses;

    public DatabaseMigrator(
        ILoggerFactory loggerFactory,
        MigratorConfiguration migratorConfiguration,
        DatabaseConfiguration databaseConfiguration
        )
    {
        _logger = loggerFactory.CreateLogger<DatabaseMigrator>();
        _loggerFactory = loggerFactory;
        _migratorConfiguration = migratorConfiguration;
        _databaseConfiguration = databaseConfiguration;
        _databaseMigrationProcesses = new List<DatabaseMigrationProcess>();
    }

    public async Task RunAsync()
    {
        if (!_migratorConfiguration.Enabled)
        {
            _logger.LogInformation("Database migrator is disabled. Skipping...");
            return;
        }

        GenerateProcesses();

        await ValidateDatabasesExist();

        if (!_migratorConfiguration.ApplyMigrations) return;

        await ApplyMigrations();
    }

    private void GenerateProcesses()
    {
        var props = _databaseConfiguration
            .GetType()
            .GetProperties();

        foreach (var prop in props)
        {
            var propertyName = prop.Name;

            var database = (DatabaseConnection)prop.GetValue(_databaseConfiguration)!;

            _databaseMigrationProcesses.Add(new DatabaseMigrationProcess(
                _loggerFactory,
                _migratorConfiguration,
                propertyName,
                database
            ));
        }
    }

    private async Task ValidateDatabasesExist()
    {
        foreach (var migrationProcess in _databaseMigrationProcesses)
        {
            await migrationProcess.ValidateDatabaseExists();
        }
    }

    private async Task ApplyMigrations()
    {
        foreach (var migrationProcess in _databaseMigrationProcesses)
        {
            try
            {
                await migrationProcess.AcquireLockAsync();

                await migrationProcess.ApplyMigrationsAsync();
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Failed to apply migrations");
                throw;
            }
            finally
            {
                await migrationProcess.ReleaseLockAsync();
            }

        }
    }
}
