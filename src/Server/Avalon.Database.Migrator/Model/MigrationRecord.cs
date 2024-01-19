using System;

namespace Avalon.Database.Migrator.Model;

public class MigrationRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[] Hash { get; set; }
    public string ExecutedBy { get; set; }
    public DateTime ExecutedOn { get; set; }
}
