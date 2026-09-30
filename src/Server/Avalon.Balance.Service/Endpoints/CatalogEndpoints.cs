using Avalon.Balance.Contract;

namespace Avalon.Balance.Service.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/catalog", (BalanceHost host) => Results.Json(host.Catalog, BalanceJson.Options));
        return routes;
    }
}
