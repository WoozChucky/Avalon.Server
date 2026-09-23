namespace Avalon.Exporter;

/// <summary>One exported artifact: the name it is selected by, and what writing it does.</summary>
internal sealed record Export(string Name, string Destination, string Summary, Action<string> Write);

/// <summary>
/// The registry. Adding an artifact the client vendors is an entry here -- not another csproj with
/// its own bootstrap, its own output convention and its own place to be forgotten in the solution.
/// </summary>
internal static class Exports
{
    /// <summary>Where the vector files live under the output root, beside the wire artifacts.</summary>
    private const string VectorsDirectory = "vectors";

    internal static readonly IReadOnlyList<Export> All =
    [
        new("proto", WireSchema.FileName,
            "the packet contracts as a language-neutral .proto schema",
            root => Lf.WriteReporting(Path.Combine(root, WireSchema.FileName), WireSchema.Generate())),

        new("opcodes", OpcodeTable.FileName,
            "the opcode-to-message mapping and per-packet encryption flags",
            root => Lf.WriteReporting(Path.Combine(root, OpcodeTable.FileName), OpcodeTable.Generate())),

        new("corpus", WireCorpus.DirectoryName + "/",
            "golden wire vectors: the bytes the server serializes each message to",
            root => WriteCorpus(Path.Combine(root, WireCorpus.DirectoryName))),

        new("crypto", SessionCryptoVectors.DirectoryName + "/" + SessionCryptoVectors.FileName,
            "known-answer vectors for the v1 session key derivation",
            root => Lf.WriteReporting(
                Path.Combine(root, SessionCryptoVectors.DirectoryName, SessionCryptoVectors.FileName),
                SessionCryptoVectors.Generate())),

        new("rotation", VectorsDirectory + "/" + ChunkRotationVectors.FileName,
            "known-answer vectors for ChunkRotation.LocalToWorld",
            root => ChunkRotationVectors.Write(
                Path.Combine(root, VectorsDirectory, ChunkRotationVectors.FileName))),

        new("object-guid", VectorsDirectory + "/" + ObjectGuidVectors.FileName,
            "known-answer vectors for the ObjectGuid (type, id) packing",
            root => ObjectGuidVectors.Write(
                Path.Combine(root, VectorsDirectory, ObjectGuidVectors.FileName))),

        new("navmesh", VectorsDirectory + "/" + NavmeshVectors.FileName,
            "known-answer vectors for the navmesh bake and its two movement queries",
            root => NavmeshVectors.Write(
                Path.Combine(root, VectorsDirectory, NavmeshVectors.FileName))),
    ];

    internal static Export? ByName(string name)
        => All.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A message that no longer exists must lose its file, or the corpus keeps asserting bytes
    /// for a packet nobody sends. Removing what the export did not produce is the only way the
    /// directory can shrink.
    /// </summary>
    private static void WriteCorpus(string directory)
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
            Lf.Write(Path.Combine(directory, name), content);
        }

        Console.WriteLine($"wrote {directory} ({files.Count} vectors files)");
    }
}
