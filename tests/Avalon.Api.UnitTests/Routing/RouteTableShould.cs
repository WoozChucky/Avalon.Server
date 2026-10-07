using Avalon.Api.Hosting.Routing;
using Xunit;

namespace Avalon.Api.UnitTests.Routing;

public sealed class RouteTableShould
{
    private static readonly RouteTable s_routes = RouteTable.Parse("""
        {
          "version": 1,
          "default": "identity",
          "internal": ["/internal"],
          "services": {
            "identity": ["/account", "/client/auth"],
            "worlds": ["/world"],
            "commerce": ["/account/purchases"],
            "distribution": ["/client/launcher"]
          }
        }
        """);

    [Theory]
    [InlineData("/world")]
    [InlineData("/world/")]
    [InlineData("/world/1")]
    [InlineData("/world/1/character/2/inventory")]
    public void Give_a_rule_its_own_path_and_every_path_under_it(string path)
    {
        Assert.Equal("worlds", s_routes.OwnerOf(path));
        Assert.Equal(new RouteRule("worlds", "/world"), s_routes.RuleFor(path));
    }

    [Theory]
    [InlineData("/worlds")]
    [InlineData("/worlds/1")]
    [InlineData("/world-map")]
    [InlineData("/worldx/1")]
    public void Match_whole_segments_only(string path)
    {
        Assert.Equal("identity", s_routes.OwnerOf(path));
        Assert.Null(s_routes.RuleFor(path));
    }

    [Theory]
    [InlineData("/account", "identity", "/account")]
    [InlineData("/account/links/steam/callback", "identity", "/account")]
    [InlineData("/account/purchases", "commerce", "/account/purchases")]
    [InlineData("/account/purchases/checkout", "commerce", "/account/purchases")]
    [InlineData("/account/purchases-history", "identity", "/account")]
    [InlineData("/client/auth/token", "identity", "/client/auth")]
    [InlineData("/client/launcher/update", "distribution", "/client/launcher")]
    public void Let_the_longest_matching_rule_decide(string path, string owner, string rule)
    {
        Assert.Equal(owner, s_routes.OwnerOf(path));
        Assert.Equal(new RouteRule(owner, rule), s_routes.RuleFor(path));
    }

    [Theory]
    [InlineData("/WORLD/1", "worlds")]
    [InlineData("/World", "worlds")]
    [InlineData("/Account/Purchases/Checkout", "commerce")]
    [InlineData("/CLIENT/Launcher", "distribution")]
    public void Ignore_case(string path, string owner) => Assert.Equal(owner, s_routes.OwnerOf(path));

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/client")]
    [InlineData("/client/channels")]
    [InlineData("/internal/game/join-tickets/redeem")]
    public void Give_a_path_no_rule_matches_to_the_default_service(string path)
    {
        Assert.Equal("identity", s_routes.OwnerOf(path));
        Assert.Null(s_routes.RuleFor(path));
    }

    [Theory]
    [InlineData("/internal", true)]
    [InlineData("/internal/game/join-tickets/redeem", true)]
    [InlineData("/Internal/Game/Sessions/End", true)]
    [InlineData("/internals", false)]
    [InlineData("/internal-game", false)]
    [InlineData("/world/1/internal", false)]
    [InlineData("/account", false)]
    public void Classify_the_paths_under_an_internal_rule_internal(string path, bool isInternal) =>
        Assert.Equal(isInternal, s_routes.IsInternal(path));

    [Theory]
    [InlineData("")]
    [InlineData("world/1")]
    public void Refuse_a_path_that_is_not_a_request_path(string path)
    {
        Assert.Throws<ArgumentException>(() => s_routes.OwnerOf(path));
        Assert.Throws<ArgumentException>(() => s_routes.IsInternal(path));
    }

    [Theory]
    [InlineData("/world", true)]
    [InlineData("/client/auth", true)]
    [InlineData("/account/game-license", true)]
    [InlineData("/v2", true)]
    [InlineData("", false)]
    [InlineData("/", false)]
    [InlineData("world", false)]
    [InlineData("/world/", false)]
    [InlineData("/World", false)]
    [InlineData("/client//auth", false)]
    [InlineData("/world/{id}", false)]
    [InlineData("/world*", false)]
    [InlineData("/world.json", false)]
    [InlineData("/world map", false)]
    [InlineData("/wörld", false)]
    public void Accept_only_rules_written_in_their_one_form(string rule, bool normalised) =>
        Assert.Equal(normalised, RouteTable.IsNormalised(rule));

    [Fact]
    public void Read_the_services_and_rules_in_the_manifests_order()
    {
        Assert.Equal("identity", s_routes.DefaultService);
        Assert.Equal(["identity", "worlds", "commerce", "distribution"], s_routes.Services);
        Assert.Equal(["/account", "/client/auth", "/world", "/account/purchases", "/client/launcher"],
            s_routes.Rules.Select(rule => rule.Prefix));
        Assert.Equal(["/internal"], s_routes.InternalRules);
    }

    [Theory]
    [InlineData("""{ "version": 1, """, "is not JSON")]
    [InlineData("""[]""", "is not a JSON object")]
    [InlineData("""{ "default": "a", "internal": [], "services": { "a": [] } }""", "\"version\": 1")]
    [InlineData("""{ "version": 2, "default": "a", "internal": [], "services": { "a": [] } }""", "\"version\": 1")]
    [InlineData("""{ "version": 1, "default": "a", "internal": [] }""", "\"services\"")]
    [InlineData("""{ "version": 1, "default": "a", "internal": [], "services": {} }""", "names no service")]
    [InlineData("""{ "version": 1, "internal": [], "services": { "a": [] } }""", "\"default\"")]
    [InlineData("""{ "version": 1, "default": "b", "internal": [], "services": { "a": [] } }""", "\"default\"")]
    [InlineData("""{ "version": 1, "default": "a", "services": { "a": [] } }""", "\"internal\"")]
    [InlineData("""{ "version": 1, "default": "a", "internal": [], "services": { "a": ["/World"] } }""", "not normalised, \"/World\"")]
    [InlineData("""{ "version": 1, "default": "a", "internal": [], "services": { "a": "/x" } }""", "as an array")]
    [InlineData("""{ "version": 1, "default": "a", "internal": [], "services": { "a": ["/x"], "b": ["/x"] } }""", "rule /x twice")]
    [InlineData("""{ "version": 1, "default": "a", "internal": ["/i", "/i"], "services": { "a": [] } }""", "internal rule /i twice")]
    [InlineData("""{ "version": 1, "default": "a", "internal": [], "services": { "a": [] }, "routes": {} }""", "\"routes\"")]
    public void Refuse_a_manifest_it_cannot_route_by(string json, string problem)
    {
        FormatException refused = Assert.Throws<FormatException>(() => RouteTable.Parse(json));

        Assert.Contains(problem, refused.Message, StringComparison.Ordinal);
    }
}
