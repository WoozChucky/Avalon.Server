namespace Avalon.ChunkGen;

internal static class RepositoryRoot
{
    /// <summary>
    /// Walks up from the build output to the directory holding Avalon.sln; when there is none, the refusal ends with
    /// <paramref name="remedy" />, what the command that asked can do instead.
    /// </summary>
    internal static string Find(string remedy)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException($"No Avalon.sln above {AppContext.BaseDirectory}. {remedy}");
    }
}
