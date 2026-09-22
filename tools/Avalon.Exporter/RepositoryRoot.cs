namespace Avalon.Exporter;

internal static class RepositoryRoot
{
    /// <summary>
    /// Walks up from the build output until it finds the solution file. The exports land in the
    /// repository whichever directory the tool was run from, because an artifact written to
    /// whatever the shell's cwd happened to be is an artifact nobody commits.
    /// </summary>
    internal static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find the repository root: no Avalon.sln above {AppContext.BaseDirectory}. " +
                "Pass --out <dir> instead.");
    }
}
