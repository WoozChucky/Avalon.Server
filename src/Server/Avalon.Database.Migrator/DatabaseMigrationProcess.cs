using System.Data;
using System.Security.Cryptography;
using System.Text;
using Avalon.Configuration;
using Avalon.Database.Migrator.Configuration;
using Avalon.Database.Migrator.Exceptions;
using Avalon.Database.Migrator.Model;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Avalon.Database.Migrator;

internal class DatabaseMigrationProcess
{
    private const string DatabaseNameMask = "[[NAME]]";
    private readonly MigratorConfiguration _config;
    private readonly DatabaseConnection _connectionDetails;
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly ILogger<DatabaseMigrationProcess> _logger;
    private MigrationLock? _currentLock;

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
        if (await IsDatabaseCreatedAsync())
        {
            return;
        }

        if (!_config.CreateDatabases)
        {
            throw new DatabaseMigrationException($"Database {_databaseName} does not exist.");
        }

        _logger.LogInformation("Database {Database} does not exist. Creating...", _databaseName);

        ICollection<MigrationScript> scripts = await ScriptReaderSystem.ListCreateScriptsAsync();

        MigrationScript? creationScript = scripts.FirstOrDefault(s => s.Name.StartsWith("00"));

        scripts.Remove(creationScript);

        await using NpgsqlConnection connection = new($"" +
                                                      $"Host={_connectionDetails.Host};" +
                                                      $"Port={_connectionDetails.Port};" +
                                                      $"Username={_connectionDetails.Username};" +
                                                      $"Password={_connectionDetails.Password};" +
                                                      $"Database=postgres;");

        await connection.OpenAsync();

        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await CreateDatabaseAsync(connection, transaction, creationScript);

        // Removed MySQL-specific USE statement; not supported in PostgreSQL.

        foreach (MigrationScript? script in scripts)
        {
            await connection.ExecuteAsync(script.Content.Replace(DatabaseNameMask, _connectionDetails.Database),
                transaction: transaction);
        }

        await transaction.CommitAsync();

        _logger.LogInformation("Database {Database} created", _databaseName);
    }

    public async Task ApplyMigrationsAsync()
    {
        ICollection<MigrationRecord> remoteRecords = await GetRemoteMigrationRecordAsync();
        ICollection<MigrationRecord> localRecords = await GetLocalMigrationRecordAsync();

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
        foreach (MigrationRecord? remoteRecord in remoteRecords)
        {
            bool foundRecord = false;

            foreach (MigrationRecord? localRecord in localRecords)
            {
                if (!remoteRecord.Name.Equals(localRecord.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                if (remoteRecord.Hash.SequenceEqual(localRecord.Hash))
                {
                    foundRecord = true;
                    break;
                }

                _logger.LogWarning(
                    "Migration {Migration} has a different hash locally. Remote has {RemoteHash} and local has {LocalHash}",
                    remoteRecord.Name,
                    BitConverter.ToString(remoteRecord.Hash.ToArray()).Replace("-", ""),
                    BitConverter.ToString(localRecord.Hash.ToArray()).Replace("-", "")
                );
            }

            if (!foundRecord)
            {
                throw new DatabaseMigrationException(
                    $"Local migration is missing locally. Remote has {remoteRecord.Name}");
            }
        }

        List<MigrationRecord> localMigrationsAhead = new();

        // Check if any missing remote migration
        foreach (MigrationRecord? localRecord in localRecords)
        {
            bool foundRecord = false;

            foreach (MigrationRecord? remoteRecord in remoteRecords)
            {
                if (!localRecord.Name.Equals(remoteRecord.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                if (!localRecord.Hash.SequenceEqual(remoteRecord.Hash))
                {
                    continue;
                }

                foundRecord = true;
                break;
            }

            if (!foundRecord)
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

    private async Task CreateDatabaseAsync(IDbConnection connection, IDbTransaction transaction,
        MigrationScript? script)
    {
        if (script == null)
        {
            throw new DatabaseMigrationException($"Database creation script for {_databaseName} not found.");
        }

        string sqlScript = script.Content.Replace(DatabaseNameMask, _connectionDetails.Database);

        await connection.ExecuteAsync(sqlScript, transaction: transaction);
    }

    private async Task<bool> IsDatabaseCreatedAsync()
    {
        try
        {
            await using NpgsqlConnection connection = new(_connectionString);

            string? record = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @Name",
                new {Name = _databaseName.ToLower()}
            );

            return !string.IsNullOrWhiteSpace(record);
        }
        catch (PostgresException e)
        {
            if (e.ErrorCode == 1049) // Unknown database
            {
                return false;
            }
        }

        return false;
    }

    private async Task<ICollection<MigrationRecord>> GetRemoteMigrationRecordAsync()
    {
        await using NpgsqlConnection connection = new(_connectionString);

        IEnumerable<MigrationRecord> records = await connection.QueryAsync<MigrationRecord>(
            "SELECT * FROM __MigrationRecord"
        );

        return records.ToList();
    }

    private async Task<ICollection<MigrationRecord>> GetLocalMigrationRecordAsync()
    {
        ICollection<string> migrationFolders = ScriptReaderSystem.ListMigrationDirectories();

        List<MigrationRecord> result = new();

        foreach (string? migrationFolder in migrationFolders)
        {
            ICollection<MigrationScript> scripts = await ScriptReaderSystem.ListMigrationScriptsAsync(migrationFolder);
            scripts = scripts.Where(s => s.Database!.Equals(_databaseName, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            if (!scripts.Any())
            {
                continue;
            }

            MigrationRecord record = new() {Name = migrationFolder, Hash = GetMigrationHash(scripts)};

            result.Add(record);
        }

        return result;
    }

    private async Task ApplyAllMigrationsAsync(ICollection<MigrationRecord> migrationRecords) =>
        await ApplyMissingMigrationsAsync(migrationRecords);

    private async Task ApplyMissingMigrationsAsync(ICollection<MigrationRecord> migrationRecords)
    {
        foreach (MigrationRecord? migrationRecord in migrationRecords)
        {
            _logger.LogInformation("Applying migration {Migration} to '{Database}'", migrationRecord.Name,
                _databaseName);

            ICollection<MigrationScript> scripts =
                await ScriptReaderSystem.ListMigrationScriptsAsync(migrationRecord.Name);
            scripts = scripts.Where(s => s.Database!.Equals(_databaseName, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            await using NpgsqlConnection connection = new(_connectionString);

            await connection.OpenAsync();

            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

            foreach (MigrationScript? script in scripts)
            {
                _logger.LogInformation("Applying script {Script} to '{Database}'...", script.Name, _databaseName);
                await connection.ExecuteAsync(script.Content, transaction: transaction);
            }

            await connection.ExecuteAsync(
                "INSERT INTO __MigrationRecord (Id, Name, Hash, ExecutedBy, ExecutedOn) VALUES (@Id, @Name, @Hash, @ExecutedBy, @ExecutedOn)",
                new
                {
                    Id = Guid.NewGuid(),
                    migrationRecord.Name,
                    migrationRecord.Hash,
                    ExecutedBy = Environment.UserName,
                    ExecutedOn = DateTime.UtcNow
                },
                transaction
            );

            await transaction.CommitAsync();

            _logger.LogInformation("Migration {Migration} applied to '{Database}' with Hash='{Hash}'",
                migrationRecord.Name,
                _databaseName,
                BitConverter.ToString(migrationRecord.Hash.ToArray()).Replace("-", "")
            );
        }
    }

    private static string GenerateConnectionString(DatabaseConnection database) =>
        $"Host={database.Host};" +
        $"Port={database.Port};" +
        $"Database={database.Database};" +
        $"Username={database.Username};" +
        $"Password={database.Password};" +
        $"Allow User Variables=true";

    private static byte[] GetMigrationHash(IEnumerable<MigrationScript> scripts)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(scripts.Select(s => s.Content).Aggregate((a, b) => a + b));
        using SHA1? sha1 = SHA1.Create();
        byte[] result = sha1.ComputeHash(bytes);
        return result;
    }

    public async Task AcquireLockAsync()
    {
        // This should check if theres a lock on the database.
        // If there is, we should wait (lets say 5 seconds) and try again up to a maximum of 10 times.
        // After failing 10 times, we should throw an exception.

        int attempts = 0;

        while (true)
        {
            await using NpgsqlConnection connection = new(_connectionString);

            IEnumerable<MigrationLock> existingLocks = await connection.QueryAsync<MigrationLock>(
                "SELECT * FROM __MigrationLock WHERE Locked = 1 AND LockedAt > @LockedAt",
                new {LockedAt = DateTime.UtcNow.AddSeconds(-5)}
            );

            if (existingLocks.Any())
            {
                if (attempts >= 10)
                {
                    throw new DatabaseMigrationException($"Failed to acquire lock for '{_databaseName}' database.");
                }

                attempts++;
                await Task.Delay(5000);
                continue;
            }

            _currentLock = new MigrationLock
            {
                Id = Guid.NewGuid(), Locked = true, LockedBy = Environment.UserName, LockedAt = DateTime.UtcNow
            };

            await connection.ExecuteAsync(
                "INSERT INTO __MigrationLock (Id, Locked, LockedBy, LockedAt) VALUES (@Id, @Locked, @LockedBy, @LockedAt)",
                new {_currentLock.Id, _currentLock.Locked, _currentLock.LockedBy, _currentLock.LockedAt}
            );

            return;
        }
    }

    public async Task ReleaseLockAsync()
    {
        if (_currentLock == null)
        {
            throw new DatabaseMigrationException($"No lock acquired for '{_databaseName}' database.");
        }

        await using NpgsqlConnection connection = new(_connectionString);

        await connection.ExecuteAsync(
            "DELETE FROM __MigrationLock WHERE Id = @Id",
            new {_currentLock.Id}
        );
    }
}
