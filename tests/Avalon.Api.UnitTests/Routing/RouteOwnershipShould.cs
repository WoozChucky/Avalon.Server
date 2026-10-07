using Avalon.Api.Authentication;
using Avalon.Api.Hosting.Routing;
using Avalon.Api.UnitTests.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Avalon.Api.UnitTests.Routing;

/// <summary>
/// The route manifest (<c>Helm/avalon-api/files/routes.json</c>) gives every endpoint of today's api the service the
/// split's design gives it (#794, design D2.3). The expected owner of each endpoint is written below, so a new endpoint
/// fails here until someone decides which service owns it.
/// </summary>
public sealed class RouteOwnershipShould
{
    private const string Manifest = "src/Server/Avalon.Api/Helm/avalon-api/files/routes.json";

    private const string Identity = "identity";
    private const string Worlds = "worlds";
    private const string Commerce = "commerce";
    private const string Distribution = "distribution";

    /// <summary>Every endpoint, as "METHOD /template", and the service that owns it.</summary>
    private static readonly Dictionary<string, string> s_owners = new(StringComparer.Ordinal)
    {
        // Every process serves these; through the ingress they reach identity, the default (design D7.3).
        ["* /health"] = Identity,
        ["* /alive"] = Identity,
        ["GET /openapi/{documentName}.json"] = Identity,
        ["GET /scalar/{documentName?}"] = Identity,
        ["GET /scalar/scalar.js"] = Identity,
        ["GET /scalar/scalar.aspnetcore.js"] = Identity,
        ["GET /scalar/favicon.svg"] = Identity,

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

    /// <summary>Paths the api answers that no endpoint covers, with their methods and owner.</summary>
    private static readonly (string Path, string[] Methods, string Owner)[] s_uncovered =
    [
        // The Steam OpenID callback: SteamOpenIdCallbackMiddleware answers it, ahead of every endpoint.
        (SteamWebLinkOptions.CallbackPath, ["GET", "POST"], Identity),
    ];

    private static RouteTable Routes() => RouteTable.Load(RepositoryRoot.PathOf(Manifest));

    [Fact]
    public async Task Decide_an_owner_for_every_endpoint_and_list_no_other()
    {
        await using ContractHost host = await ContractHost.StartAsync();
        var mapped = Endpoints(host).Select(endpoint => endpoint.Key).ToList();

        string[] undecided = mapped.Except(s_owners.Keys, StringComparer.Ordinal).ToArray();
        string[] unmapped = s_owners.Keys.Except(mapped, StringComparer.Ordinal).ToArray();

        Assert.True(undecided.Length == 0,
            "Decide which service owns each new endpoint (design D2.3) and add it to RouteOwnershipShould: "
            + string.Join(", ", undecided));
        Assert.True(unmapped.Length == 0, "RouteOwnershipShould lists endpoints the api no longer maps: " + string.Join(", ", unmapped));
        Assert.Equal(mapped.Count, mapped.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Give_every_endpoint_the_owner_the_design_gives_it()
    {
        await using ContractHost host = await ContractHost.StartAsync();
        RouteTable routes = Routes();

        string[] wrong = Endpoints(host)
            .Where(endpoint => s_owners.ContainsKey(endpoint.Key))
            .Select(endpoint => (endpoint.Key, endpoint.Path, Owner: routes.OwnerOf(endpoint.Path)))
            .Where(endpoint => endpoint.Owner != s_owners[endpoint.Key])
            .Select(endpoint => $"{endpoint.Key} ({endpoint.Path}) goes to {endpoint.Owner}, not {s_owners[endpoint.Key]}")
            .ToArray();

        Assert.True(wrong.Length == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public async Task Reach_each_endpoint_by_the_path_it_is_checked_with()
    {
        await using ContractHost host = await ContractHost.StartAsync();

        foreach (Mapped endpoint in Endpoints(host))
        {
            Endpoint? reached = await host.EndpointReachedAsync(endpoint.ProbeMethod, endpoint.Path);

            Assert.True(reached is RouteEndpoint route && Describe(route) == endpoint,
                $"{endpoint.ProbeMethod} {endpoint.Path} reaches {reached?.DisplayName ?? "nothing"}, not {endpoint.Key}");
        }
    }

    [Fact]
    public async Task Own_the_paths_no_endpoint_covers()
    {
        await using ContractHost host = await ContractHost.StartAsync();
        RouteTable routes = Routes();
        var mapped = Endpoints(host).ToHashSet();

        foreach ((string path, string[] methods, string owner) in s_uncovered)
        {
            Assert.Equal(owner, routes.OwnerOf(path));
            Assert.False(routes.IsInternal(path), path);
            foreach (string method in methods)
            {
                // Routing may still pick its own 405 endpoint for a method no endpoint here takes; only a mapped one covers it.
                Endpoint? reached = await host.EndpointReachedAsync(method, path);
                Assert.False(reached is RouteEndpoint route && mapped.Contains(Describe(route)),
                    $"{method} {path} reaches {reached?.DisplayName}; list it with the endpoints instead");
            }
        }
    }

    [Fact]
    public async Task Classify_the_internal_endpoints_internal_and_no_other()
    {
        await using ContractHost host = await ContractHost.StartAsync();
        RouteTable routes = Routes();

        Assert.All(Endpoints(host), endpoint => Assert.True(s_internal.Contains(endpoint.Key) == routes.IsInternal(endpoint.Path),
            $"{endpoint.Key}: internal by the design = {s_internal.Contains(endpoint.Key)}, by the manifest = {routes.IsInternal(endpoint.Path)}"));
    }

    [Fact]
    public async Task Leave_no_rule_dead()
    {
        await using ContractHost host = await ContractHost.StartAsync();
        RouteTable routes = Routes();
        var paths = Endpoints(host).Select(endpoint => endpoint.Path)
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

    [Fact]
    public void Sample_each_route_parameter_constraint()
    {
        Assert.Equal("/world/1/character/1", RouteSamples.PathFor("world/{worldId:int}/character/{id}"));
        Assert.Equal("/pat/admin/account/1", RouteSamples.PathFor("pat/admin/account/{accountId:long}"));
        Assert.Equal("/admin/purchases/" + RouteSamples.Guid + "/refund", RouteSamples.PathFor("admin/purchases/{id:guid}/refund"));
        Assert.Equal("/world/1/map-template/chunk-asset/a/b.obj", RouteSamples.PathFor("world/{worldId:int}/map-template/chunk-asset/{*filename}"));
        Assert.Equal("/openapi/1.json", RouteSamples.PathFor("/openapi/{documentName}.json"));
        Assert.Throws<InvalidOperationException>(() => RouteSamples.PathFor("x/{id:alpha}"));
    }

    private static IEnumerable<Mapped> Endpoints(ContractHost host) => host.Endpoints.OfType<RouteEndpoint>().Select(Describe);

    /// <summary>
    /// An endpoint by value, since minimal-API data sources build new endpoint objects each time they are read: its key,
    /// "METHOD /template" ("*" for any method), its sample path, the method a probe sends, and its display name.
    /// </summary>
    private static Mapped Describe(RouteEndpoint endpoint)
    {
        IReadOnlyList<string>? methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        string method = methods is null || methods.Count == 0 ? "*" : string.Join(',', methods);
        string template = "/" + endpoint.RoutePattern.RawText?.TrimStart('/');
        return new Mapped(method + " " + template, RouteSamples.PathFor(endpoint.RoutePattern), methods?.FirstOrDefault() ?? "GET",
            endpoint.DisplayName);
    }

    private sealed record Mapped(string Key, string Path, string ProbeMethod, string? DisplayName);
}
