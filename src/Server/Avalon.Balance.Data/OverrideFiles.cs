using System.Text.Json;
using Avalon.Balance.Core;

namespace Avalon.Balance.Data;

public static class OverrideFiles
{
    public static OverrideReport Apply(SeedTables tables, string path, bool required)
    {
        using JsonDocument? document = Read(path, required);
        return document is null ? OverrideReport.None : Overrides.Apply(tables, document.RootElement);
    }

    /// <summary>The file as JSON, or null when it is absent and not <paramref name="required" />. The caller disposes it.</summary>
    /// <exception cref="FileNotFoundException">The file is required and absent.</exception>
    /// <exception cref="InvalidDataException">The file is not valid JSON.</exception>
    public static JsonDocument? Read(string path, bool required)
    {
        if (!File.Exists(path))
        {
            return required ? throw new FileNotFoundException($"Overrides file '{path}' not found", path) : null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Overrides file '{path}' is not valid JSON: {e.Message}", e);
        }
    }
}
