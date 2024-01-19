using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalon.Database.Migrator.Model;

internal class MigrationRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[] Hash { get; set; }
    public string ExecutedBy { get; set; }
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
