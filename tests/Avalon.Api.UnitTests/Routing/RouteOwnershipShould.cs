using Avalon.Api.Hosting.Routing;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.UnitTests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Avalon.Api.UnitTests.Routing;

/// <summary>
/// Every endpoint of the API has one owner (#794, design D2.3), and three places agree on it: the table below, which
/// decides it, so a new endpoint fails here until someone decides which service owns it; the service's process, run
/// alone as a deployment runs it, which maps exactly the endpoints the table gives it, beside those every process
/// maps; and the route manifest (<c>Helm/avalon-api/files/routes.json</c>), which sends each of them to that service.
/// </summary>
public sealed class RouteOwnershipShould
{
    private const string Manifest = "src/Server/Avalon.Api/Helm/avalon-api/files/routes.json";

    private const string Identity = "identity";
    private const string Worlds = "worlds";
    private const string Commerce = "commerce";
    private const string Distribution = "distribution";

    /// <summary>
    /// What every process maps, whatever its services, as "METHOD /template": through the ingress these reach the
    /// manifest's default service, identity (design D7.3).
    /// </summary>
    private static readonly HashSet<string> s_everyProcess = new(StringComparer.Ordinal)
    {
        "* /health",
        "* /alive",
        "GET /openapi/{documentName}.json",
        "GET /scalar/{documentName?}",
        "GET /scalar/scalar.js",
        "GET /scalar/scalar.aspnetcore.js",
        "GET /scalar/favicon.svg",
    };

    /// <summary>Every endpoint of a service, as "METHOD /template", and the service that owns it.</summary>
    private static readonly Dictionary<string, string> s_owners = new(StringComparer.Ordinal)
    {
        // identity: accounts, credentials, MFA, personal access tokens, sessions, links, game sign-in and admission.
        ["GET /account"] = Identity,
        ["GET /account/{id:long}"] = Identity,
        ["GET /account/paginate"] = Identity,
        ["POST /account/authenticate"] = Identity,
        ["POST /account/register"] = Identity,
        ["POST /account/password"] = Identity,
        ["POST /account/email/change"] = Identity,
        ["POST /account/email/confirm"] = Identity,
        ["POST /account/logout"] = Identity,
        ["PATCH /account/{id:long}/status"] = Identity,
        ["PATCH /account/{id:long}/roles"] = Identity,
        ["DELETE /account/{id:long}/mfa"] = Identity,
        ["GET /account/email/verification"] = Identity,
        ["POST /account/email/verification"] = Identity,
        ["POST /account/email/verification/confirm"] = Identity,
        ["POST /account/refresh"] = Identity,
        ["GET /account/links/{pendingLinkId:guid}"] = Identity,
        ["POST /account/links/confirm"] = Identity,
        ["POST /account/links/{pendingLinkId:guid}/cancel"] = Identity,
        ["POST /account/links/steam/start"] = Identity,
        ["POST /account/links/steam/confirm"] = Identity,
        ["GET /account/links/steam/challenge/{id:guid}"] = Identity,
        ["GET /account/links/steam/{id:guid}"] = Identity,
        ["GET /account/links/steam/consolidations/pending"] = Identity,
        ["GET /account/links/steam/consolidations/{id:guid}"] = Identity,
        ["POST /account/links/steam/consolidations/{id:guid}/resume"] = Identity,
        ["POST /mfa/setup"] = Identity,
        ["POST /mfa/confirm"] = Identity,
        ["POST /mfa/reset"] = Identity,
        ["POST /mfa/verify"] = Identity,
        ["GET /mfa/status"] = Identity,
        ["GET /pat"] = Identity,
        ["POST /pat"] = Identity,
        ["GET /pat/{id:long}"] = Identity,
        ["DELETE /pat/{id:long}"] = Identity,
        ["GET /pat/admin"] = Identity,
        ["POST /pat/admin"] = Identity,
        ["DELETE /pat/admin/account/{accountId:long}"] = Identity,
        ["POST /notification/register"] = Identity,
        ["POST /client/auth/code"] = Identity,
        ["POST /client/auth/game-ticket"] = Identity,
        ["POST /client/auth/token"] = Identity,
        ["POST /client/auth/refresh"] = Identity,
        ["POST /client/auth/revoke"] = Identity,
        ["GET /client/auth/sessions"] = Identity,
        ["DELETE /client/auth/sessions/{familyId:guid}"] = Identity,
        ["POST /client/auth/provider-attempts"] = Identity,
        ["POST /client/auth/store/proof"] = Identity,
        ["POST /client/auth/handoffs/redeem"] = Identity,
        ["POST /client/auth/game-context/refresh"] = Identity,
        ["POST /client/auth/game-context/logout"] = Identity,
        ["POST /client/auth/links/proposal"] = Identity,
        ["POST /client/auth/links/complete"] = Identity,
        ["POST /game/worlds"] = Identity,
        ["POST /game/join-tickets"] = Identity,
        ["POST /game/reconnect-tickets"] = Identity,
        ["POST /internal/game/join-tickets/redeem"] = Identity,
        ["POST /internal/game/sessions/activate"] = Identity,
        ["POST /internal/game/sessions/heartbeat"] = Identity,
        ["POST /internal/game/sessions/end"] = Identity,

        // worlds: the world registry and maintenance, world content and its live editing, characters, presence,
        // public tooltips and previews, the balance proxy.
        ["GET /world"] = Worlds,
        ["POST /world"] = Worlds,
        ["GET /world/{id}"] = Worlds,
        ["PATCH /world/{id}"] = Worlds,
        ["GET /world/{id}/maintenance"] = Worlds,
        ["POST /world/{id}/maintenance"] = Worlds,
        ["DELETE /world/{id}/maintenance"] = Worlds,
        ["GET /world/{worldId:int}/item-template"] = Worlds,
        ["GET /world/{worldId:int}/item-template/{id:long}"] = Worlds,
        ["PUT /world/{worldId:int}/item-template/{id:long}"] = Worlds,
        ["GET /world/{worldId:int}/ability-template"] = Worlds,
        ["GET /world/{worldId:int}/ability-template/{id:long}"] = Worlds,
        ["PUT /world/{worldId:int}/ability-template/{id:long}"] = Worlds,
        ["GET /world/{worldId:int}/creature-template"] = Worlds,
        ["GET /world/{worldId:int}/creature-template/{id:long}"] = Worlds,
        ["PUT /world/{worldId:int}/creature-template/{id:long}"] = Worlds,
        ["GET /world/{worldId:int}/aura-template"] = Worlds,
        ["GET /world/{worldId:int}/aura-template/{id:long}"] = Worlds,
        ["PUT /world/{worldId:int}/aura-template/{id:long}"] = Worlds,
        ["GET /world/{worldId:int}/character/paginate"] = Worlds,
        ["GET /world/{worldId:int}/character/{id}"] = Worlds,
        ["PATCH /world/{worldId:int}/character/{id}"] = Worlds,
        ["GET /world/{worldId:int}/character/{id}/inventory"] = Worlds,
        ["GET /world/{worldId:int}/character/{id}/abilities"] = Worlds,
        ["GET /world/{worldId:int}/character/{id}/stats"] = Worlds,
        ["GET /world/{worldId:int}/character/{id}/quests"] = Worlds,
        ["GET /world/{worldId:int}/character/{id}/auras"] = Worlds,
        ["GET /world/{worldId:int}/map-template"] = Worlds,
        ["GET /world/{worldId:int}/map-template/{id:int}"] = Worlds,
        ["GET /world/{worldId:int}/map-template/{id:int}/preview-layout"] = Worlds,
        ["GET /world/{worldId:int}/map-template/chunk-asset/{*filename}"] = Worlds,
        ["GET /world/{worldId:int}/quest-template"] = Worlds,
        ["GET /world/{worldId:int}/quest-template/{id:long}"] = Worlds,
        ["GET /world/{worldId:int}/observability/character/{id}"] = Worlds,
        ["GET /world/{worldId:int}/scripts"] = Worlds,
        ["GET /character"] = Worlds,
        ["GET /observability/online"] = Worlds,
        ["GET /observability/instance/{instanceId:guid}"] = Worlds,
        ["GET /public/world"] = Worlds,
        ["GET /public/world/{worldId:int}/item/{id:long}"] = Worlds,
        ["GET /public/world/{worldId:int}/ability/{id:long}"] = Worlds,
        ["GET /public/preview/item/{id:long}"] = Worlds,
        ["GET /public/preview/ability/{id:long}"] = Worlds,
        ["GET /balance/catalog"] = Worlds,
        ["POST /balance/runs"] = Worlds,
        ["GET /balance/runs/{id}"] = Worlds,
        ["DELETE /balance/runs/{id}"] = Worlds,
        ["POST /balance/exports"] = Worlds,

        // commerce: checkout, purchases and licences, the admin purchase tools, payment webhooks.
        ["GET /account/game-license"] = Commerce,
        ["GET /account/purchases/{id:guid}"] = Commerce,
        ["POST /account/purchases/checkout"] = Commerce,
        ["GET /admin/purchases"] = Commerce,
        ["GET /admin/purchases/{id:guid}"] = Commerce,
        ["POST /admin/purchases/{id:guid}/refund"] = Commerce,
        ["POST /admin/purchases/{id:guid}/retry"] = Commerce,
        ["POST /payments/notifications/{provider}"] = Commerce,

        // distribution: launcher self-update, releases, changelog, channels and their manifests.
        ["GET /client/launcher"] = Distribution,
        ["GET /client/launcher/update"] = Distribution,
        ["GET /client/releases"] = Distribution,
        ["GET /client/changelog"] = Distribution,
        ["GET /client/channels"] = Distribution,
        ["GET /client/channels/{channel}/manifest"] = Distribution,
    };

    /// <summary>The endpoints served only inside the cluster, on the workload listener (design D7.4).</summary>
    private static readonly HashSet<string> s_internal = new(StringComparer.Ordinal)
    {
        "POST /internal/game/join-tickets/redeem",
        "POST /internal/game/sessions/activate",
        "POST /internal/game/sessions/heartbeat",
        "POST /internal/game/sessions/end",
    };

    /// <summary>Paths the API answers that no endpoint covers, and their owner.</summary>
    private static readonly (string Path, string Owner)[] s_uncovered =
    [
        // The Steam OpenID callback: SteamOpenIdCallbackMiddleware answers it, ahead of every endpoint.
        (SteamWebLinkOptions.CallbackPath, Identity),
    ];

    public static TheoryData<string> Services => new(ApiServices.All.Select(service => service.Name));

    private static RouteTable Routes() => RouteTable.Load(RepositoryRoot.PathOf(Manifest));

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Map_the_endpoints_the_table_gives_the_service_and_route_each_to_it(string service)
    {
        RouteTable routes = Routes();
        await using WebApplication process = ApiProcess.Build(ApiServices.All.Single(candidate => candidate.Name == service));
        var mapped = Endpoints(process).ToList();
        string[] owned = s_owners.Where(row => row.Value == service).Select(row => row.Key).ToArray();

        string[] undecided = mapped.Where(key => !s_owners.ContainsKey(key) && !s_everyProcess.Contains(key)).ToArray();
        string[] others = mapped.Where(key => s_owners.TryGetValue(key, out string? owner) && owner != service)
            .Select(key => $"{key} (the table gives it to {s_owners[key]})")
            .ToArray();
        string[] unmapped = owned.Concat(s_everyProcess).Except(mapped, StringComparer.Ordinal).ToArray();
        string[] misrouted = owned
            .Select(key => (Key: key, Path: PathOf(key)))
            .Where(endpoint => routes.OwnerOf(endpoint.Path) != service || routes.IsInternal(endpoint.Path) != s_internal.Contains(endpoint.Key))
            .Select(endpoint => $"{endpoint.Key} ({endpoint.Path}): the manifest sends it to {routes.OwnerOf(endpoint.Path)}"
                + $" and calls it internal = {routes.IsInternal(endpoint.Path)}")
            .ToArray();

        Assert.True(undecided.Length == 0,
            "Decide which service owns each new endpoint (design D2.3) and add it to RouteOwnershipShould: " + string.Join(", ", undecided));
        Assert.True(others.Length == 0, $"The {service} process maps endpoints of other services: " + string.Join(", ", others));
        Assert.True(unmapped.Length == 0, $"RouteOwnershipShould lists endpoints the {service} process does not map: " + string.Join(", ", unmapped));
        Assert.True(misrouted.Length == 0, string.Join(Environment.NewLine, misrouted));
        Assert.Equal(mapped.Count, mapped.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Send_what_every_process_maps_and_the_paths_no_endpoint_covers_to_their_owner()
    {
        RouteTable routes = Routes();
        IEnumerable<(string Path, string Owner)> paths = s_everyProcess.Select(key => (PathOf(key), routes.DefaultService))
            .Concat(s_uncovered);

        Assert.All(paths, path =>
        {
            Assert.Equal(path.Owner, routes.OwnerOf(path.Path));
            Assert.False(routes.IsInternal(path.Path), path.Path);
        });
    }

    [Fact]
    public void Leave_no_rule_dead()
    {
        RouteTable routes = Routes();
        var paths = s_owners.Keys.Concat(s_everyProcess).Select(PathOf)
            .Concat(s_uncovered.Select(uncovered => uncovered.Path))
            .ToList();

        var deciding = paths.Select(routes.RuleFor).OfType<RouteRule>().ToHashSet();
        string[] dead = routes.Rules.Where(rule => !deciding.Contains(rule)).Select(rule => $"{rule.Prefix} ({rule.Service})")
            .Concat(routes.InternalRules.Where(rule => !paths.Any(path => RouteTable.Matches(rule, path))).Select(rule => $"{rule} (internal)"))
            .ToArray();

        Assert.True(dead.Length == 0, "Rules that decide no endpoint's owner: " + string.Join(", ", dead));
    }

    [Fact]
    public void Write_every_rule_in_its_one_form_and_once()
    {
        RouteTable routes = Routes();
        string[] rules = routes.Rules.Select(rule => rule.Prefix).Concat(routes.InternalRules).ToArray();

        Assert.All(rules, rule => Assert.True(RouteTable.IsNormalised(rule), rule));
        Assert.Equal(rules.Length, rules.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Identity, routes.DefaultService);
        Assert.Equal([Identity, Worlds, Commerce, Distribution], routes.Services);
    }

    /// <summary>The sample path of an endpoint written "METHOD /template".</summary>
    private static string PathOf(string key) => RouteSamples.PathFor(key[(key.IndexOf(' ', StringComparison.Ordinal) + 1)..]);

    /// <summary>The endpoints <paramref name="process"/> maps, as "METHOD /template" ("*" for any method).</summary>
    private static IEnumerable<string> Endpoints(WebApplication process) =>
        ApiProcess.Endpoints(process).Select(endpoint =>
        {
            IReadOnlyList<string>? methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            string method = methods is null || methods.Count == 0 ? "*" : string.Join(',', methods);
            return method + " /" + endpoint.RoutePattern.RawText?.TrimStart('/');
        });
}
