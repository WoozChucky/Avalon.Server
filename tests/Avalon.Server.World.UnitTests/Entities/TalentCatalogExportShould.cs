using Avalon.Exporter;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The talent tree the client vendors (#1181): schema/talents/talent-catalog-v1.json is the seed whole, so the client
/// never draws a node, an edge or a stat line the world does not hold.
/// </summary>
public class TalentCatalogExportShould
{
    [Fact]
    public void Match_the_committed_catalog()
    {
        string path = Path.Combine(RepositoryLayout.Root(), "schema", TalentCatalogExport.DirectoryName,
            TalentCatalogExport.FileName);
        string committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(string.Equals(committed, TalentCatalogExport.Render(), StringComparison.Ordinal),
            $"schema/{TalentCatalogExport.DirectoryName}/{TalentCatalogExport.FileName} no longer matches the seed. " +
            $"Re-export it:{Environment.NewLine}    dotnet run --project tools/Avalon.Exporter -- talent-catalog");
    }
}
