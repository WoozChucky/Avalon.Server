using System.Reflection;
using Xunit;
using Avalon.Balance.Core;

namespace Avalon.Balance.UnitTests;

/// <summary>The simulator library the balance service will host never reaches the world server or a database (balance workbench, sub-project 2).</summary>
public class BalanceCoreAssemblyShould
{
    [Fact]
    public void Not_reference_the_world_server_or_a_database()
    {
        string[] referenced = typeof(BalanceRunner).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalon.World", StringComparison.Ordinal) && n != "Avalon.World.Public");
        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalon.Server", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalon.Database", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Reference_only_the_combat_project()
    {
        string csproj = Path.Combine(RepositoryRoot(), "src", "Server", "Avalon.Balance.Core", "Avalon.Balance.Core.csproj");
        string[] references = System.Xml.Linq.XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileName(r.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToArray();

        Assert.Equal(["Avalon.Combat.csproj"], references);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Avalon.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Avalon.sln not found above " + AppContext.BaseDirectory);
    }
}
