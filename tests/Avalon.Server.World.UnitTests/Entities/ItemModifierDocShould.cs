using Avalon.Exporter;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// What the modifier catalog's committed files say (#1179): docs/item-modifiers.md is hand-written around one block
/// generated from the seed, and schema/items/item-modifier-catalog-v1.json, which the client vendors, is the seed whole.
/// Both must be the seed's, so neither tells a reader or a client a value the game does not roll.
/// </summary>
public class ItemModifierDocShould
{
    [Fact]
    public void Match_the_seed_in_the_committed_page()
    {
        string path = Path.Combine(RepositoryLayout.Root(), ItemModifierDoc.DocsDirectory, ItemModifierDoc.FileName);
        string page = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(string.Equals(page, ItemModifierDoc.Splice(page, ItemModifierDoc.Render()), StringComparison.Ordinal),
            $"The generated block of docs/{ItemModifierDoc.FileName} no longer matches the seed. Regenerate it:" +
            $"{Environment.NewLine}    dotnet run --project tools/Avalon.Exporter -- item-modifier-doc");
    }

    [Fact]
    public void Match_the_seed_in_the_committed_catalog_export()
    {
        string path = Path.Combine(RepositoryLayout.Root(), "schema", ItemModifierCatalogExport.DirectoryName,
            ItemModifierCatalogExport.FileName);
        string committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(string.Equals(committed, ItemModifierCatalogExport.Render(), StringComparison.Ordinal),
            $"schema/{ItemModifierCatalogExport.DirectoryName}/{ItemModifierCatalogExport.FileName} no longer matches the " +
            $"seed. Re-export it:{Environment.NewLine}    dotnet run --project tools/Avalon.Exporter -- item-modifier-catalog");
    }
}
