namespace Avalon.Server.World.UnitTests;

/// <summary>The checked-in files a guard test reads in place, outside any project.</summary>
internal static class RepositoryLayout
{
    /// <summary>The directory holding Avalon.sln, found by walking up from the test output.</summary>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
