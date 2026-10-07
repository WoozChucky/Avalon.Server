using System.Text;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Avalon.Api.UnitTests.Routing;

/// <summary>
/// A request path an endpoint's route template matches, so the route manifest can be asked who owns every endpoint
/// (#794): <c>int</c> and <c>long</c> parameters take 1, <c>guid</c> ones <see cref="Guid"/>, a catch-all
/// <see cref="CatchAll"/> and an unconstrained parameter 1. A constraint with no sample fails, so a new one is decided
/// here.
/// </summary>
public static class RouteSamples
{
    public const string Guid = "0f8fad5b-d9cb-469f-a165-70867728950e";
    public const string CatchAll = "a/b.obj";
    public const string Number = "1";

    /// <summary>The sample path for <paramref name="template"/>, for example <c>world/{worldId:int}/character/{id}</c>.</summary>
    public static string PathFor(string template) => PathFor(RoutePatternFactory.Parse(template));

    /// <summary>The sample path for <paramref name="pattern"/>, always starting with '/'.</summary>
    public static string PathFor(RoutePattern pattern)
    {
        var path = new StringBuilder();
        foreach (RoutePatternPathSegment segment in pattern.PathSegments)
        {
            path.Append('/');
            foreach (RoutePatternPart part in segment.Parts)
            {
                path.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => ValueFor(parameter),
                    _ => throw new InvalidOperationException($"{pattern.RawText} has a part RouteSamples does not know."),
                });
            }
        }

        return path.Length == 0 ? "/" : path.ToString();
    }

    private static string ValueFor(RoutePatternParameterPart parameter)
    {
        if (parameter.IsCatchAll)
        {
            return CatchAll;
        }

        string?[] constraints = parameter.ParameterPolicies.Select(policy => policy.Content).ToArray();
        return constraints switch
        {
            [] or ["int"] or ["long"] => Number,
            ["guid"] => Guid,
            _ => throw new InvalidOperationException(
                $"RouteSamples has no value for {{{parameter.Name}:{string.Join(':', constraints)}}}; give it one."),
        };
    }
}
