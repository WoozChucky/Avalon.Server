using System.Globalization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The game workload routes, under <c>/internal</c>, are mapped on the game workload listener only (#794, design D7.4):
/// each requires the listener's port in the request's host (<c>RequireHost("*:&lt;port&gt;")</c>), so a request that
/// reaches the public listener, the ingress included, matches none of them and is answered 404. Who may call them is
/// still decided by the workload authentication (<see cref="GameServerAuthHandler"/>).
/// </summary>
public sealed class GameInternalRoutes(int port) : IApplicationModelConvention
{
    /// <summary>The routes' first segment, which the route manifest lists as internal-only.</summary>
    public const string Segment = "internal";

    public void Apply(ApplicationModel application)
    {
        var host = new HostAttribute("*:" + port.ToString(CultureInfo.InvariantCulture));
        foreach (SelectorModel selector in application.Controllers.SelectMany(controller => controller.Selectors))
        {
            if (IsInternal(selector.AttributeRouteModel?.Template))
                selector.EndpointMetadata.Add(host);
        }
    }

    private static bool IsInternal(string? template)
    {
        string path = template?.TrimStart('~', '/') ?? string.Empty;
        return path.Equals(Segment, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(Segment + "/", StringComparison.OrdinalIgnoreCase);
    }
}
