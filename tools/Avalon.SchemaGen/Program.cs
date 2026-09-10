using Avalon.SchemaGen;

// Writes the exported wire schema, the opcode table, and the golden corpus. The destination
// defaults to the schema/ directory at the repository root; pass a path to override it.
string outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(RepositoryRoot(), "schema");

Directory.CreateDirectory(outputDirectory);

Write(Path.Combine(outputDirectory, WireSchema.FileName), WireSchema.Generate());
Write(Path.Combine(outputDirectory, OpcodeTable.FileName), OpcodeTable.Generate());
WriteCorpus(Path.Combine(outputDirectory, WireCorpus.DirectoryName));

return 0;

// A message that no longer exists must lose its file, or the corpus keeps asserting bytes
// for a packet nobody sends. Removing what the export did not produce is the only way the
// directory can shrink.
static void WriteCorpus(string directory)
{
    Directory.CreateDirectory(directory);

    IReadOnlyDictionary<string, string> files = WireCorpus.Generate();

    foreach (string stale in Directory.EnumerateFiles(directory, "*.txt")
                 .Where(path => !files.ContainsKey(Path.GetFileName(path))))
    {
        File.Delete(stale);
        Console.WriteLine($"removed {stale}");
    }

    foreach ((string name, string content) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
    {
        File.WriteAllText(Path.Combine(directory, name), content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    Console.WriteLine($"wrote {directory} ({files.Count} vectors files)");
}

// Written with explicit LF so the file is byte-identical whichever platform emits it.
static void Write(string path, string content)
{
    File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    Console.WriteLine($"wrote {path} ({content.Split('\n').Length} lines)");
}

static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new InvalidOperationException(
            $"Could not find the repository root: no Avalon.sln above {AppContext.BaseDirectory}. " +
            "Pass the output directory as an argument instead.");
}
