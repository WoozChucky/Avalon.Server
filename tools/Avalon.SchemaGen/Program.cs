using Avalon.SchemaGen;

// Writes the exported wire schema and opcode table. The destination defaults to the
// schema/ directory at the repository root; pass a path to override it.
string outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(RepositoryRoot(), "schema");

Directory.CreateDirectory(outputDirectory);

Write(Path.Combine(outputDirectory, WireSchema.FileName), WireSchema.Generate());
Write(Path.Combine(outputDirectory, OpcodeTable.FileName), OpcodeTable.Generate());

return 0;

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
