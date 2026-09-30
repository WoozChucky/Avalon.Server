namespace Avalon.Balance.Service.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/catalog", (BalanceHost host) => Results.Ok(host.Catalog));
        return routes;
    }
}
