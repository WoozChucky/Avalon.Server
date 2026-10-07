using System.Xml.Linq;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// The API's services are libraries that share only the hosting and the contract (#794, design section 2.2): no
/// service library references another, directly or through a project it references, and neither
/// Avalon.Api.Hosting nor Avalon.Api.Contract references a service. Only the host, Avalon.Api, brings them together, so
/// a service's process never carries, or comes to depend on, another service's code.
/// </summary>
public sealed class ApiServiceBoundariesShould
{
    private static readonly string[] s_shared = ["Avalon.Api.Hosting", "Avalon.Api.Contract"];

    [Fact]
    public void Keep_every_service_library_out_of_the_others_and_out_of_the_shared_ones()
    {
        string[] services = ApiServices.All.Select(service => service.ControllerAssembly.GetName().Name!).ToArray();

        string[] crossings = services.Concat(s_shared)
            .SelectMany(project => References(project)
                .Where(reference => reference != project && services.Contains(reference, StringComparer.Ordinal))
                .Select(reference => $"{project} -> {reference}"))
            .ToArray();

        Assert.True(crossings.Length == 0, "References between the API's services: " + string.Join(", ", crossings));
    }

    /// <summary>The projects <paramref name="project"/> references, directly or through the projects it references.</summary>
    private static HashSet<string> References(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([RepositoryRoot.PathOf($"src/Server/{project}/{project}.csproj")]);
        while (pending.TryPop(out string? path))
        {
            string directory = Path.GetDirectoryName(path)!;
            foreach (XElement reference in XDocument.Load(path).Descendants("ProjectReference"))
            {
                string referenced = Path.GetFullPath(Path.Combine(directory,
                    reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
                if (seen.Add(Path.GetFileNameWithoutExtension(referenced)))
                    pending.Push(referenced);
            }
        }

        return seen;
    }
}
