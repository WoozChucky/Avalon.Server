namespace Avalon.ChunkGen;

internal static class RepositoryRoot
{
    /// <summary>Walks up from the build output to the directory holding Avalon.sln.</summary>
    internal static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException($"No Avalon.sln above {AppContext.BaseDirectory}. Pass --maps <dir> instead.");
    }
}
