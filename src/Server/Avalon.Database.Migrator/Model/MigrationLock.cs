using System;

namespace Avalon.Database.Migrator.Model;

public class MigrationLock
{
    public Guid Id { get; set; }
    public bool Locked { get; set; }
    public string LockedBy { get; set; } = string.Empty;
    public DateTime LockedAt { get; set; }
}
