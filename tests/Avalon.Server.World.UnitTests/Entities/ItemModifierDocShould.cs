using Avalon.Exporter;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The modifier catalog's page (#1179): docs/item-modifiers.md is hand-written around one block generated from the seed,
/// and the committed block must be the seed's, so the page never tells a reader a value the game does not roll.
/// </summary>
public class ItemModifierDocShould
{
    private const string RegenerateCommand = "dotnet run --project tools/Avalon.Exporter -- item-modifier-doc";

    [Fact]
    public void Match_the_seed_in_the_committed_page()
    {
        string path = Path.Combine(RepositoryRoot(), ItemModifierDoc.DocsDirectory, ItemModifierDoc.FileName);
        string page = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(string.Equals(page, ItemModifierDoc.Splice(page, ItemModifierDoc.Render()), StringComparison.Ordinal),
            $"The generated block of docs/{ItemModifierDoc.FileName} no longer matches the seed. Regenerate it:" +
            $"{Environment.NewLine}    {RegenerateCommand}");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
