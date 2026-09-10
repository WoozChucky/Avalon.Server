using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalon.SchemaGen;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Reaches the checked-in golden corpus. The tests read the files rather than regenerating
/// the fixtures, so what they verify is the artifact a client would be handed.
/// </summary>
internal static class Corpus
{
    internal const string RegenerateCommand = "dotnet run --project tools/Avalon.SchemaGen";

    internal static string Directory =>
        Path.Combine(RepositoryLayout.Root(), "schema", WireCorpus.DirectoryName);

    /// <summary>One case per message, so a failure names the packet rather than the run.</summary>
    public static TheoryData<string> Messages()
    {
        TheoryData<string> data = [];

        foreach (Type contract in WireSchema.ContractTypes())
        {
            data.Add(WireSchema.SchemaNameOf(contract));
        }

        return data;
    }

    internal static Type ContractFor(string message) =>
        WireSchema.ContractTypes().Single(contract =>
            string.Equals(WireSchema.SchemaNameOf(contract), message, StringComparison.Ordinal));

    internal static IReadOnlyList<WireVector> VectorsFor(string message)
    {
        string path = Path.Combine(Directory, message + ".txt");

        Assert.True(
            File.Exists(path),
            $"schema/{WireCorpus.DirectoryName}/{message}.txt is missing. Regenerate it with:"
            + $"{Environment.NewLine}    {RegenerateCommand}");

        IReadOnlyList<WireVector> vectors = WireCorpus.Parse(message, File.ReadAllText(path));

        Assert.True(vectors.Count > 0, $"schema/{WireCorpus.DirectoryName}/{message}.txt holds no vectors.");

        return vectors;
    }
}
