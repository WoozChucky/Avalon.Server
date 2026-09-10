using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalon.SchemaGen;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Holds the checked-in wire schema to the C# packet contracts it was exported from.
/// A contract change that is not re-exported would otherwise be invisible until a client
/// deserialized a field into the wrong property, which raises nothing.
/// </summary>
public class WireSchemaShould
{
    private const string RegenerateCommand = "dotnet run --project tools/Avalon.SchemaGen";

    [Fact]
    public void Match_The_Contracts_It_Was_Exported_From()
    {
        AssertMatchesCheckedInFile(WireSchema.FileName, WireSchema.Generate());
    }

    [Fact]
    public void Describe_Every_Opcode_The_Contracts_Declare()
    {
        AssertMatchesCheckedInFile(OpcodeTable.FileName, OpcodeTable.Generate());
    }

    private static void AssertMatchesCheckedInFile(string fileName, string generated)
    {
        string path = Path.Combine(RepositoryRoot(), "schema", fileName);

        Assert.True(
            File.Exists(path),
            $"schema/{fileName} is missing. Create it with:{Environment.NewLine}    {RegenerateCommand}");

        string[] checkedIn = Lines(File.ReadAllText(path));
        string[] regenerated = Lines(generated);

        if (checkedIn.SequenceEqual(regenerated, StringComparer.Ordinal))
        {
            return;
        }

        Assert.Fail(Explain(fileName, checkedIn, regenerated));
    }

    /// <summary>
    /// Says which line diverged, what precedes it, and what it should have been, so the
    /// reader can tell a contract they meant to change from one they did not.
    /// </summary>
    /// <remarks>
    /// Only the first difference is reported. An inserted or removed line shifts every line
    /// after it, so a count of differing lines would say "451" for a one-field change and
    /// read as a catastrophe.
    /// </remarks>
    private static string Explain(string fileName, string[] checkedIn, string[] regenerated)
    {
        int shared = Math.Min(checkedIn.Length, regenerated.Length);

        int first = 0;
        while (first < shared && string.Equals(checkedIn[first], regenerated[first], StringComparison.Ordinal))
        {
            first++;
        }

        var report = new List<string>
        {
            $"schema/{fileName} no longer matches the C# packet contracts.",
            string.Empty,
            "If you changed a contract on purpose, re-export the schema and commit it:",
            $"    {RegenerateCommand}",
            string.Empty,
            $"First difference at line {first + 1}"
                + $" (checked in has {checkedIn.Length} lines, generated has {regenerated.Length}).",
        };

        const int contextLines = 3;
        for (int i = Math.Max(0, first - contextLines); i < first; i++)
        {
            report.Add($"    {i + 1,5} | {checkedIn[i]}");
        }

        report.Add($"    {first + 1,5} < {Describe(checkedIn, first)}      (checked in)");
        report.Add($"    {first + 1,5} > {Describe(regenerated, first)}      (generated)");

        return string.Join(Environment.NewLine, report);
    }

    private static string Describe(string[] lines, int index) =>
        index < lines.Length ? lines[index] : "<end of file>";

    // Compares content rather than encoding: a working tree checked out with CRLF is not drift.
    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find the repository root: no Avalon.sln above {AppContext.BaseDirectory}.");
    }
}
