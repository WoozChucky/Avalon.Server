using Avalon.Api.Hosting.Routing;
using Xunit;

namespace Avalon.Api.UnitTests.Routing;

/// <summary>
/// The read-only smoke check (<c>tools/api-smoke</c>, #794) goes through every rule of the route manifest
/// (<c>Helm/avalon-api/files/routes.json</c>), so a smoke run after a route move reaches every route group: each
/// service's rules decide the owner of at least one request of that service's group, the internal rules have a request,
/// and each request is in the group of the service the manifest sends it to, so checking one group checks one service.
/// </summary>
public sealed class SmokeCoverageShould
{
    private const string Manifest = "src/Server/Avalon.Api/Helm/avalon-api/files/routes.json";
    private const string Requests = "tools/api-smoke/requests.tsv";

    private static readonly string[] s_auth = ["none", "pat"];

    [Fact]
    public void Send_a_request_through_every_rule_from_the_group_of_its_service()
    {
        var routes = RouteTable.Load(RepositoryRoot.PathOf(Manifest));
        List<string> problems = [];
        List<(string Group, string Path)> requests = [];
        foreach (string line in File.ReadAllLines(RepositoryRoot.PathOf(Requests)))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length != 5 || fields[1] != "GET" || !fields[2].StartsWith('/') || !s_auth.Contains(fields[4]))
            {
                problems.Add($"\"{line}\" is not a GET line of group, method, path, expected statuses and none or pat, tab-separated");
                continue;
            }

            string path = fields[2].Split('?')[0];
            string owner = routes.OwnerOf(path);
            if (fields[0] != owner)
                problems.Add($"{path} is in the group {fields[0]}, but the manifest sends it to {owner}");
            requests.Add((fields[0], path));
        }

        problems.AddRange(routes.Rules
            .Where(rule => !requests.Any(request => request.Group == rule.Service && routes.RuleFor(request.Path) == rule))
            .Select(rule => $"no {rule.Service} request goes through {rule.Prefix}"));
        problems.AddRange(routes.InternalRules
            .Where(rule => !requests.Any(request => RouteTable.Matches(rule, request.Path)))
            .Select(rule => $"no request goes to the internal {rule}"));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
