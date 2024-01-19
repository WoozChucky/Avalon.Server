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
    
    private static string GenerateConnectionString(DatabaseConnection database)
    {
        return $"Server={database.Host};" +
               $"Port={database.Port};" +
               $"Database={database.Database};" +
               $"Uid={database.Username};" +
               $"Pwd={database.Password};";
    }
    
    private static byte[] GetMigrationHash(IEnumerable<MigrationScript> scripts)
    {
        var bytes = Encoding.UTF8.GetBytes(scripts.Select(s => s.Content).Aggregate((a, b) => a + b));
        using var sha1 = SHA1.Create();
        var result = sha1.ComputeHash(bytes);
        return result;
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
        var migrationFolders = await ScriptReaderSystem.ListMigrationDirectoriesAsync();

        var result = new List<MigrationRecord>();
        
        foreach (var migrationFolder in migrationFolders)
        {
            var scripts = await ScriptReaderSystem.ListMigrationScriptsAsync(migrationFolder);
            scripts = scripts.Where(s => s.Database!.Equals(_databaseName, StringComparison.InvariantCultureIgnoreCase)).ToList();
            
            var record = new MigrationRecord
            {
                Name = migrationFolder,
                Hash = GetMigrationHash(scripts)
            };

            result.Add(record);
        }

        return result;
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
            
        var localMigrationsBehind = localRecords.Except(remoteRecords, new MigrationRecordComparer()).ToList();
            
        if (localMigrationsBehind.Any())
        {
            throw new DatabaseMigrationException(
                $"Local migrations are behind remote migrations. " +
                $"Please make sure local migrations are updated for {_databaseName} database."
            );
        }
        
        var localMigrationsAhead = remoteRecords.Except(localRecords, new MigrationRecordComparer()).ToList();
        
        if (localMigrationsAhead.Any())
        {
            _logger.LogInformation("Local migrations are ahead of remote migrations. Applying missing migrations...");
            await ApplyMissingMigrationsAsync(localMigrationsAhead);
        }
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
            
            _logger.LogInformation("Migration {Migration} applied to '{Database}'", migrationRecord.Name, _databaseName);
        }
    }
}
