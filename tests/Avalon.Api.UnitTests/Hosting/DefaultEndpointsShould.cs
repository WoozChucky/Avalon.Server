using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// Kubernetes probes the api on /alive and /health, and pods run in Production. The endpoints
/// used to be mapped only in Development, which left every probe failing in the cluster.
/// </summary>
public class DefaultEndpointsShould
{
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task Answer_healthy_in_production(string path)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.AddDefaultHealthChecks();
        await using WebApplication app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();

        HttpResponseMessage response = await app.GetTestClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Only the aggregate status: no check names or exception text leak to the caller.
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
