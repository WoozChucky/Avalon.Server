namespace Avalon.Balance;

public static class RepositoryRoot
{
    /// <summary>Walks up from the build output to the folder holding Avalon.sln.</summary>
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException($"No Avalon.sln above {AppContext.BaseDirectory}.");
    }
}
