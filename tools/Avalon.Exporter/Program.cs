using Avalon.Exporter;

// Selects artifacts by name and writes them. With no arguments it lists what it could write and
// writes nothing -- exporting everything by default would make a bare run a six-artifact diff
// nobody asked for.
//
//   dotnet run --project tools/Avalon.Exporter
//   dotnet run --project tools/Avalon.Exporter -- all
//   dotnet run --project tools/Avalon.Exporter -- proto corpus
//   dotnet run --project tools/Avalon.Exporter -- all --out /tmp/schema

const string OutOption = "--out";

var names = new List<string>();
string? outputRoot = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case OutOption:
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"{OutOption} needs a directory.");
                return 1;
            }

            outputRoot = Path.GetFullPath(args[++i]);
            break;

        case "-h" or "--help":
            Usage();
            return 0;

        default:
            if (args[i].StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                Usage(Console.Error);
                return 1;
            }

            names.Add(args[i]);
            break;
    }
}

if (names.Count == 0)
{
    Usage();
    return 0;
}

// Every name is resolved before anything is written. A typo that exported five of six artifacts
// and reported a failure afterwards would leave the tree in a state no one asked for.
List<Export> selected;

if (names.Count == 1 && names[0].Equals("all", StringComparison.OrdinalIgnoreCase))
{
    selected = [.. Exports.All];
}
else if (names.Any(name => name.Equals("all", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("'all' cannot be combined with individual artifact names.");
    return 1;
}
else
{
    selected = [];

    foreach (string name in names)
    {
        Export? export = Exports.ByName(name);

        if (export is null)
        {
            Console.Error.WriteLine($"Unknown artifact '{name}'. Nothing was written.");
            Usage(Console.Error);
            return 1;
        }

        if (!selected.Contains(export)) selected.Add(export);
    }
}

string root = outputRoot ?? Path.Combine(RepositoryRoot.Find(), "schema");
Directory.CreateDirectory(root);

foreach (Export export in selected)
{
    export.Write(root);
}

Console.WriteLine($"exported {selected.Count} of {Exports.All.Count} artifacts to {root}");
return 0;

static void Usage(TextWriter? writer = null)
{
    writer ??= Console.Out;

    writer.WriteLine("Exports the artifacts the client vendors from the server's own types.");
    writer.WriteLine();
    writer.WriteLine("  dotnet run --project tools/Avalon.Exporter -- <artifact>... [--out <dir>]");
    writer.WriteLine("  dotnet run --project tools/Avalon.Exporter -- all");
    writer.WriteLine();
    writer.WriteLine("Artifacts:");

    int width = Exports.All.Max(export => export.Name.Length);

    foreach (Export export in Exports.All)
    {
        writer.WriteLine($"  {export.Name.PadRight(width)}  {export.Destination}");
        writer.WriteLine($"  {new string(' ', width)}  {export.Summary}");
    }

    writer.WriteLine();
    writer.WriteLine("  all  every artifact above");
    writer.WriteLine();
    writer.WriteLine("Output defaults to schema/ at the repository root. Nothing is written without");
    writer.WriteLine("an artifact name.");
}
