namespace Avalon.Api.UnitTests;

/// <summary>The checkout the tests run from, for the files they hold the code to.</summary>
internal static class RepositoryRoot
{
    /// <summary>The full path of <paramref name="relativePath"/>, its segments separated by '/', under the repository root.</summary>
    public static string PathOf(string relativePath)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Avalon.sln")))
            {
                return Path.Combine([dir.FullName, .. relativePath.Split('/')]);
            }
        }

        throw new InvalidOperationException("Avalon.sln not found above " + AppContext.BaseDirectory);
    }
}
