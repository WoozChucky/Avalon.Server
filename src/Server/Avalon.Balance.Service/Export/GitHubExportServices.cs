using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Avalon.Balance.Service.Export;

public static class GitHubExportServices
{
    /// <summary>
    /// Registers <see cref="IGitHub" /> as a typed client. AddServiceDefaults gives every HttpClient the standard
    /// resilience handler, which retries; a write must go out once, so it is removed from this client.
    /// </summary>
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental; the test proving a write is sent once guards it.
    public static IHttpClientBuilder AddGitHubExport(this IServiceCollection services) =>
        services.AddHttpClient<IGitHub, GitHubRest>((sp, client) =>
            {
                BalanceServiceOptions options = sp.GetRequiredService<IOptions<BalanceServiceOptions>>().Value;
                // No token: exports answer 503 before this client is used; the empty token is never sent.
                GitHubRest.Configure(client, options.Repository, options.GitHubToken ?? "");
            })
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
}
