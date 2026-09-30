using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Avalon.Balance.Service.UnitTests;

internal static class BalanceTestHost
{
    public const string Secret = "0123456789abcdef0123456789abcdef-secret";

    public static WebApplication Build(string? secret = Secret)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        if (secret is not null)
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Balance:SharedSecret"] = secret });
        return BalanceServiceHost.Build(builder);
    }
}
