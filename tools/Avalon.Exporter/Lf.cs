namespace Avalon.Exporter;

/// <summary>
/// Every artifact this tool writes is hashed as bytes at the other end, so all of them are written
/// with explicit LF regardless of the platform emitting them. A CRLF slipping into one file is not
/// a formatting nit: it is a hash the client cannot reproduce.
/// </summary>
internal static class Lf
{
    internal static void Write(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>As <see cref="Write"/>, and reports what it wrote.</summary>
    internal static void WriteReporting(string path, string content)
    {
        Write(path, content);
        Console.WriteLine($"wrote {path} ({content.Split('\n').Length} lines)");
    }
}
