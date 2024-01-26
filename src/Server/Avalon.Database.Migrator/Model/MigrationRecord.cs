using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalon.Database.Migrator.Model;

/// <summary>
/// Represents a migration record.
/// </summary>
internal class MigrationRecord
{
    /// <summary>
    /// Gets or sets the unique identifier of the property.
    /// </summary>
    /// <value>
    /// The identifier.
    /// </value>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the migration.
    /// </summary>
    /// <value>
    /// The name of the property.
    /// </value>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hash value of the migration.
    /// </summary>
    /// <value>
    /// The hash value represented as an array of bytes.
    /// </value>
    public byte[] Hash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the name of the entity that executed the operation.
    /// </summary>
    /// <value>
    /// The name of the entity that executed the operation.
    /// </value>
    public string ExecutedBy { get; set; } = string.Empty;

    /// <summary>
    /// Property representing the date and time when the operation was executed.
    /// </summary>
    /// <value>
    /// The value of this property is a DateTime object representing the date and time when the operation was executed.
    /// </value>
    public DateTime ExecutedOn { get; set; }
}

internal class MigrationRecordComparer : IEqualityComparer<MigrationRecord>
{
    public bool Equals(MigrationRecord x, MigrationRecord y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        if (x.GetType() != y.GetType()) return false;

        return x.Name == y.Name && x.Hash.SequenceEqual(y.Hash);
    }

    public int GetHashCode(MigrationRecord obj)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 23 + obj.Name.GetHashCode();

            foreach (var b in obj.Hash)
            {
                hash = hash * 23 + b.GetHashCode();
            }

            return hash;
        }
    }
}
