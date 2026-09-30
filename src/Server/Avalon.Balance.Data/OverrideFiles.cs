using System.Text.Json;
using Avalon.Balance.Core;

namespace Avalon.Balance.Data;

public static class OverrideFiles
{
    public static OverrideReport Apply(SeedTables tables, string path, bool required)
    {
        if (!File.Exists(path))
        {
            return required ? throw new FileNotFoundException($"Overrides file '{path}' not found", path) : OverrideReport.None;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Overrides file '{path}' is not valid JSON: {e.Message}", e);
        }

        using (document)
            return Overrides.Apply(tables, document.RootElement);
    }
}
