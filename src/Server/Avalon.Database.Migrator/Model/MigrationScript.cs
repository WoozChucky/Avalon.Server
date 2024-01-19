namespace Avalon.Database.Migrator.Model;

internal class MigrationScript
{
    public string Name { get; set; } = string.Empty;
    public string Migration { get; set; } = string.Empty;
    public string? Database { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
