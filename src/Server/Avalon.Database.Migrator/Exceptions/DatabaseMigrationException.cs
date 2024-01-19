using System;

namespace Avalon.Database.Migrator.Exceptions;

public class DatabaseMigrationException : Exception
{
    public DatabaseMigrationException(string message) : base(message)
    {
    }

    public DatabaseMigrationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
