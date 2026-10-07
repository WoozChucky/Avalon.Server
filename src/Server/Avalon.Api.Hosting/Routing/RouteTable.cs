using System.Globalization;
using System.Text.Json;

namespace Avalon.Api.Hosting.Routing;

/// <summary>
/// Which service owns a request path, as the route manifest says (<c>Helm/avalon-api/files/routes.json</c>; #794,
/// design sections 2.3 and 7.1). A rule <c>/x</c> matches <c>/x</c> and every path under it, ignoring case; the longest
/// matching rule decides; a path no rule matches belongs to the default service. Internal rules mark the paths served
/// only inside the cluster, which no public route names.
/// </summary>
public sealed class RouteTable
{
    /// <summary>The manifest version this reader understands.</summary>
    public const int SupportedVersion = 1;

    private static readonly string[] s_members = ["version", "default", "internal", "services"];

    private RouteTable(string defaultService, IReadOnlyList<string> services, IReadOnlyList<RouteRule> rules,
        IReadOnlyList<string> internalRules)
    {
        DefaultService = defaultService;
        Services = services;
        Rules = rules;
        InternalRules = internalRules;
    }

    /// <summary>The service that owns every path no rule matches.</summary>
    public string DefaultService { get; }

    /// <summary>The services, in the manifest's order.</summary>
    public IReadOnlyList<string> Services { get; }

    /// <summary>Every service's rules, in the manifest's order.</summary>
    public IReadOnlyList<RouteRule> Rules { get; }

    /// <summary>The rules whose paths are served only inside the cluster.</summary>
    public IReadOnlyList<string> InternalRules { get; }

    /// <summary>The manifest in the file at <paramref name="path"/>.</summary>
    public static RouteTable Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// The manifest <paramref name="json"/>, or a <see cref="FormatException"/> naming what is wrong: not JSON, a
    /// version other than <see cref="SupportedVersion"/>, a member it does not know, a default that is not one of its
    /// services, a rule that is not normalised (<see cref="IsNormalised"/>), or a rule listed twice.
    /// </summary>
    public static RouteTable Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The route manifest is not JSON.", exception);
        }

        using (document)
        {
            return Read(document.RootElement);
        }
    }

    /// <summary>
    /// Whether <paramref name="rule"/> is written the one way the manifest accepts: a <c>/</c>, then segments of
    /// lower-case ASCII letters, digits and hyphens separated by single <c>/</c>, with no <c>/</c> at the end. Matching
    /// ignores case anyway; the form keeps every rule comparable and lets the chart put a rule into a Traefik path
    /// regexp as it is.
    /// </summary>
    public static bool IsNormalised(string rule)
    {
        if (rule.Length < 2 || rule[0] != '/' || rule[^1] == '/')
        {
            return false;
        }

        for (int i = 1; i < rule.Length; i++)
        {
            char c = rule[i];
            bool segmentCharacter = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
            if (!segmentCharacter && (c != '/' || rule[i - 1] == '/'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="rule"/> matches <paramref name="path"/>: the path is the rule or lies under it, ignoring case.</summary>
    public static bool Matches(string rule, string path) =>
        path.StartsWith(rule, StringComparison.OrdinalIgnoreCase) && (path.Length == rule.Length || path[rule.Length] == '/');

    /// <summary>The service that owns <paramref name="path"/>, a request path without its query string.</summary>
    public string OwnerOf(string path) => RuleFor(path)?.Service ?? DefaultService;

    /// <summary>The rule that decides who owns <paramref name="path"/>, the longest that matches, or null when none does.</summary>
    public RouteRule? RuleFor(string path)
    {
        RequireRequestPath(path);
        RouteRule? longest = null;
        foreach (RouteRule rule in Rules)
        {
            if (Matches(rule.Prefix, path) && (longest is null || rule.Prefix.Length > longest.Prefix.Length))
            {
                longest = rule;
            }
        }

        return longest;
    }

    /// <summary>Whether <paramref name="path"/> is served only inside the cluster.</summary>
    public bool IsInternal(string path)
    {
        RequireRequestPath(path);
        return InternalRules.Any(rule => Matches(rule, path));
    }

    private static RouteTable Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("is not a JSON object");
        }

        foreach (JsonProperty member in root.EnumerateObject())
        {
            if (!s_members.Contains(member.Name, StringComparer.Ordinal))
            {
                throw Invalid($"has a member it does not know, \"{member.Name}\"");
            }
        }

        if (!root.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int number) || number != SupportedVersion)
        {
            throw Invalid("needs \"version\": " + SupportedVersion.ToString(CultureInfo.InvariantCulture));
        }

        if (!root.TryGetProperty("services", out JsonElement servicesElement) || servicesElement.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("needs \"services\", an object of service names and their rules");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var services = new List<string>();
        var rules = new List<RouteRule>();
        foreach (JsonProperty service in servicesElement.EnumerateObject())
        {
            services.Add(service.Name);
            foreach (string prefix in ReadRules(service.Value, $"service {service.Name}"))
            {
                if (!seen.Add(prefix))
                {
                    throw Invalid($"lists the rule {prefix} twice");
                }

                rules.Add(new RouteRule(service.Name, prefix));
            }
        }

        if (services.Count == 0)
        {
            throw Invalid("names no service");
        }

        if (!root.TryGetProperty("default", out JsonElement defaultElement) || defaultElement.ValueKind != JsonValueKind.String
            || !services.Contains(defaultElement.GetString()!, StringComparer.Ordinal))
        {
            throw Invalid("needs \"default\", the name of one of its services");
        }

        if (!root.TryGetProperty("internal", out JsonElement internalElement))
        {
            throw Invalid("needs \"internal\", its internal-only rules");
        }

        var internalSeen = new HashSet<string>(StringComparer.Ordinal);
        var internalRules = new List<string>();
        foreach (string prefix in ReadRules(internalElement, "internal"))
        {
            if (!internalSeen.Add(prefix))
            {
                throw Invalid($"lists the internal rule {prefix} twice");
            }

            internalRules.Add(prefix);
        }

        return new RouteTable(defaultElement.GetString()!, services, rules, internalRules);
    }

    private static List<string> ReadRules(JsonElement element, string owner)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"needs the rules of {owner} as an array");
        }

        var rules = new List<string>();
        foreach (JsonElement rule in element.EnumerateArray())
        {
            string? prefix = rule.ValueKind == JsonValueKind.String ? rule.GetString() : null;
            if (prefix is null || !IsNormalised(prefix))
            {
                throw Invalid($"has a rule of {owner} that is not normalised, {rule.GetRawText()}");
            }

            rules.Add(prefix);
        }

        return rules;
    }

    private static void RequireRequestPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            throw new ArgumentException($"A request path starts with '/', and \"{path}\" does not.", nameof(path));
        }
    }

    private static FormatException Invalid(string problem) => new("The route manifest " + problem + ".");
}

/// <summary>A route manifest rule: a path prefix and the service its paths go to.</summary>
public sealed record RouteRule(string Service, string Prefix);
