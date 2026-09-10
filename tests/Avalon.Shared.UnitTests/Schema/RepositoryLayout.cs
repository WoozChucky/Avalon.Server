using System;
using System.IO;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Locates the checked-in files the schema tests read, which live outside any project and so
/// are not found relative to the test assembly.
/// </summary>
internal static class RepositoryLayout
{
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find the repository root: no Avalon.sln above {AppContext.BaseDirectory}.");
    }
}
