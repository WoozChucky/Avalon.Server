using Avalon.Balance.Core;
using Avalon.Balance.Reporting;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class CheckedInFilesShould
{
    private static string Root => RepositoryRoot.Find();

    [Theory]
    [InlineData("scenarios.json")]
    [InlineData("targets.json")]
    [InlineData("rotations.json")]
    public void Be_in_the_canonical_form_Save_writes(string file)
    {
        (string s, string t, string r) = ConfigFiles.Save(TestData.Config());
        string expected = file switch { "scenarios.json" => s, "targets.json" => t, _ => r };

        Assert.Equal(expected, File.ReadAllText(Path.Combine(Root, "balance", file)));
    }

    [Fact]
    public void Give_the_command_lines_csv_for_a_small_fixed_run()
    {
        var request = new RunRequest(null, null,
            new RunFilter(new HashSet<CharacterClass> { CharacterClass.Warrior }, null, null, new HashSet<string> { "normal-3" }),
            RunsPerRow: 20, Seed: 1);

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), request, null, CancellationToken.None);

        Assert.Equal(RunStatus.Done, result.Status);
        string golden = File.ReadAllText(Path.Combine(Root, "tests", "Avalon.Balance.UnitTests", "Golden", "warrior-normal3-20runs.csv"));
        Assert.Equal(golden, CsvReport.Render(result.Rows, result.Grades));
    }
}
