using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalon.Configuration;
using Avalon.Database.Migrator.Configuration;
using Avalon.Database.Migrator.Exceptions;
using Avalon.Database.Migrator.Model;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Avalon.Database.Migrator;

internal class DatabaseMigrationProcess
{
    private readonly ILogger<DatabaseMigrationProcess> _logger;
    private readonly MigratorConfiguration _config;
    private readonly string _databaseName;
    private readonly DatabaseConnection _connectionDetails;
    private readonly string _connectionString;
    private MigrationLock? _currentLock;

    private const string DatabaseNameMask = "[[NAME]]";

    public DatabaseMigrationProcess(
        ILoggerFactory loggerFactory,
        MigratorConfiguration config,
        string databaseName,
        DatabaseConnection connectionDetails
        )
    {
        _logger = loggerFactory.CreateLogger<DatabaseMigrationProcess>();
        _config = config;
        _databaseName = databaseName;
        _connectionDetails = connectionDetails;
        _connectionString = GenerateConnectionString(_connectionDetails);
    }

    public async Task ValidateDatabaseExists()
    {
        if (await IsDatabaseCreatedAsync()) return;

        if (!_config.CreateDatabases) throw new DatabaseMigrationException($"Database {_databaseName} does not exist.");

        _logger.LogInformation("Database {Database} does not exist. Creating...", _databaseName);

        var scripts = await ScriptReaderSystem.ListCreateScriptsAsync();

        var creationScript = scripts.FirstOrDefault(s => s.Name.StartsWith("00"));

        scripts.Remove(creationScript);

        await using var connection = new MySqlConnection(
            $"Server={_connectionDetails.Host};" +
            $"Port={_connectionDetails.Port};" +
            $"Database=mysql;" +
            $"Uid={_connectionDetails.Username};" +
            $"Pwd={_connectionDetails.Password};"
        );

        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        await CreateDatabaseAsync(connection, transaction, creationScript);

        await connection.ExecuteAsync("USE " + _connectionDetails.Database, transaction: transaction);

        foreach (var script in scripts)
        {
            await connection.ExecuteAsync(script.Content.Replace(DatabaseNameMask, _connectionDetails.Database), transaction: transaction);
        }

        await transaction.CommitAsync();

        _logger.LogInformation("Database {Database} created", _databaseName);
    }

    public async Task ApplyMigrationsAsync()
    {
        var remoteRecords = await GetRemoteMigrationRecordAsync();
        var localRecords = await GetLocalMigrationRecordAsync();

        if (!remoteRecords.Any())
        {
            _logger.LogInformation("No migrations found in '{Database}'. Applying all migrations...", _databaseName);
            await ApplyAllMigrationsAsync(localRecords);
            return;
        }

        // Remote migrations may be behind, in this case we need to apply the missing migrations.
        // In this case, we need to check if the local migrations are ahead of the remote migrations.
        // We also need to check if the local migrations are behind the remote migrations.
        // If the local migrations are behind, we throw an exception.

        // Check if any missing local migration
        foreach (var remoteRecord in remoteRecords)
        {
            var foundRecord = false;

            foreach (var localRecord in localRecords)
            {
                if (!remoteRecord.Name.Equals(localRecord.Name, StringComparison.InvariantCultureIgnoreCase)) continue;

                if (remoteRecord.Hash.SequenceEqual(localRecord.Hash))
                {
                    foundRecord = true;
                    break;
                }

                _logger.LogWarning("Migration {Migration} has a different hash locally. Remote has {RemoteHash} and local has {LocalHash}",
                    remoteRecord.Name,
                    BitConverter.ToString(remoteRecord.Hash.ToArray()).Replace("-", ""),
                    BitConverter.ToString(localRecord.Hash.ToArray()).Replace("-", "")
                );
            }

            if (foundRecord == false)
            {
                throw new DatabaseMigrationException($"Local migration is missing locally. Remote has {remoteRecord.Name}");
            }
        }

        var localMigrationsAhead = new List<MigrationRecord>();

        // Check if any missing remote migration
        foreach (var localRecord in localRecords)
        {
            var foundRecord = false;

            foreach (var remoteRecord in remoteRecords)
            {
                if (!localRecord.Name.Equals(remoteRecord.Name, StringComparison.InvariantCultureIgnoreCase)) continue;
                if (!localRecord.Hash.SequenceEqual(remoteRecord.Hash)) continue;
                foundRecord = true;
                break;
            }

            if (foundRecord == false)
            {
                localMigrationsAhead.Add(localRecord);
            }
        }

        if (localMigrationsAhead.Any())
        {
            _logger.LogInformation("Local migrations are ahead of remote migrations. Applying missing migrations...");
            await ApplyMissingMigrationsAsync(localMigrationsAhead);
        }
    }

    private async Task CreateDatabaseAsync(IDbConnection connection, IDbTransaction transaction, MigrationScript? script)
    {
        if (script == null)
            throw new DatabaseMigrationException($"Database creation script for {_databaseName} not found.");

        var sqlScript = script.Content.Replace(DatabaseNameMask, _connectionDetails.Database);

        await connection.ExecuteAsync(sqlScript, transaction: transaction);
    }

    private async Task<bool> IsDatabaseCreatedAsync()
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);

            var record = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @Name",
                new { Name = _databaseName.ToLower() }
            );

            return !string.IsNullOrWhiteSpace(record);
        }
        catch (MySqlException e)
        {
            if (e.ErrorCode == MySqlErrorCode.UnknownDatabase)
                return false;
        }

        return false;
    }

    private async Task<ICollection<MigrationRecord>> GetRemoteMigrationRecordAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);

        var records = await connection.QueryAsync<MigrationRecord>(
            "SELECT * FROM __MigrationRecord"
        );

        return records.ToList();
    }

    private async Task<ICollection<MigrationRecord>> GetLocalMigrationRecordAsync()
    {
        var migrationFolders = ScriptReaderSystem.ListMigrationDirectories();

        var result = new List<MigrationRecord>();

        foreach (var migrationFolder in migrationFolders)
        {
            var scripts = await ScriptReaderSystem.ListMigrationScriptsAsync(migrationFolder);
            scripts = scripts.Where(s => s.Database!.Equals(_databaseName, StringComparison.InvariantCultureIgnoreCase)).ToList();

            if (!scripts.Any()) continue;

            var record = new MigrationRecord
            {
                Name = migrationFolder,
                Hash = GetMigrationHash(scripts)
            };

            result.Add(record);
        }

        return result;
    }

    private async Task ApplyAllMigrationsAsync(ICollection<MigrationRecord> migrationRecords)
    {
        await ApplyMissingMigrationsAsync(migrationRecords);
    }

    private async Task ApplyMissingMigrationsAsync(ICollection<MigrationRecord> migrationRecords)
    {
        foreach (var migrationRecord in migrationRecords)
        {
            _logger.LogInformation("Applying migration {Migration} to '{Database}'", migrationRecord.Name, _databaseName);

            var scripts = await ScriptReaderSystem.ListMigrationScriptsAsync(migrationRecord.Name);
            scripts = scripts.Where(s => s.Database!.Equals(_databaseName, StringComparison.InvariantCultureIgnoreCase)).ToList();

            await using var connection = new MySqlConnection(_connectionString);

            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            foreach (var script in scripts)
            {
                _logger.LogInformation("Applying script {Script} to '{Database}'...", script.Name, _databaseName);
                await connection.ExecuteAsync(script.Content, transaction: transaction);
            }

            await connection.ExecuteAsync(
                "INSERT INTO __MigrationRecord (Id, Name, Hash, ExecutedBy, ExecutedOn) VALUES (@Id, @Name, @Hash, @ExecutedBy, @ExecutedOn)",
                new
                {
                    Id = Guid.NewGuid(),
                    Name = migrationRecord.Name,
                    Hash = migrationRecord.Hash,
                    ExecutedBy = Environment.UserName,
                    ExecutedOn = DateTime.UtcNow,
                },
                transaction: transaction
            );

            await transaction.CommitAsync();

            _logger.LogInformation("Migration {Migration} applied to '{Database}' with Hash='{Hash}'",
                migrationRecord.Name,
                _databaseName,
                BitConverter.ToString(migrationRecord.Hash.ToArray()).Replace("-", "")
            );
        }
    }

    private static string GenerateConnectionString(DatabaseConnection database)
    {
        return $"Server={database.Host};" +
               $"Port={database.Port};" +
               $"Database={database.Database};" +
               $"Uid={database.Username};" +
               $"Pwd={database.Password};" +
               $"Allow User Variables=true";
    }

    private static byte[] GetMigrationHash(IEnumerable<MigrationScript> scripts)
    {
        var bytes = Encoding.UTF8.GetBytes(scripts.Select(s => s.Content).Aggregate((a, b) => a + b));
        using var sha1 = SHA1.Create();
        var result = sha1.ComputeHash(bytes);
        return result;
    }

    public async Task AcquireLockAsync()
    {
        // This should check if theres a lock on the database.
        // If there is, we should wait (lets say 5 seconds) and try again up to a maximum of 10 times.
        // After failing 10 times, we should throw an exception.

        var attempts = 0;

        while (true)
        {
            await using var connection = new MySqlConnection(_connectionString);

            var existingLocks = await connection.QueryAsync<MigrationLock>(
                "SELECT * FROM __MigrationLock WHERE Locked = 1 AND LockedAt > @LockedAt",
                new { LockedAt = DateTime.UtcNow.AddSeconds(-5) }
            );

            if (existingLocks.Any())
            {
                if (attempts >= 10)
                    throw new DatabaseMigrationException($"Failed to acquire lock for '{_databaseName}' database.");

                attempts++;
                await Task.Delay(5000);
                continue;
            }

            _currentLock = new MigrationLock
            {
                Id = Guid.NewGuid(),
                Locked = true,
                LockedBy = Environment.UserName,
                LockedAt = DateTime.UtcNow,
            };

            await connection.ExecuteAsync(
                "INSERT INTO __MigrationLock (Id, Locked, LockedBy, LockedAt) VALUES (@Id, @Locked, @LockedBy, @LockedAt)",
                new
                {
                    Id = _currentLock.Id,
                    Locked = _currentLock.Locked,
                    LockedBy = _currentLock.LockedBy,
                    LockedAt = _currentLock.LockedAt,
                }
            );

            return;
        }
    }

    public async Task ReleaseLockAsync()
    {
        if (_currentLock == null)
            throw new DatabaseMigrationException($"No lock acquired for '{_databaseName}' database.");

        await using var connection = new MySqlConnection(_connectionString);

        await connection.ExecuteAsync(
            "DELETE FROM __MigrationLock WHERE Id = @Id",
            new { Id = _currentLock.Id }
        );
    }
}
