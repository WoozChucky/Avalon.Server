using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Hosting;

/// <summary>
/// A process runs the API services <c>Application:Services</c> names (#794, design section 2.2): every service of the
/// host when the setting is unset, as before the split; otherwise those it names, without regard to case and in the
/// host's order, with their controllers and no others. A list with no name, or with a name the host has no service
/// for, stops startup naming the setting.
/// </summary>
public sealed class ApiServiceSelectionShould
{
    [Theory]
    [InlineData("{}", "identity,worlds,commerce,distribution")]
    [InlineData("""{ "Application": { "Services": ["Worlds"] } }""", "worlds")]
    [InlineData("""{ "Application": { "Services": ["distribution", "IDENTITY"] } }""", "identity,distribution")]
    [InlineData("""{ "Application": { "Services": "commerce" } }""", "commerce")]
    public async Task Run_the_services_the_setting_names_or_every_one_when_it_is_unset(string settings, string expected)
    {
        await using WebApplication app = Builder(settings).Build();

        IReadOnlyList<IApiService> running = app.Services.GetRequiredService<ApiServiceSelection>().Services;
        Assert.Equal(expected, string.Join(",", running.Select(service => service.Name)));
        Assert.Equal(running.Select(service => service.ControllerAssembly.GetName().Name),
            app.Services.GetRequiredService<ApplicationPartManager>().ApplicationParts.OfType<AssemblyPart>().Select(part => part.Name));
    }

    [Theory]
    [InlineData("""{ "Application": { "Services": [] } }""")]
    [InlineData("""{ "Application": { "Services": "" } }""")]
    [InlineData("""{ "Application": { "Services": ["worlds", "billing"] } }""")]
    public void Stop_startup_naming_the_setting_when_the_list_is_empty_or_names_no_service(string settings)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Builder(settings));

        Assert.Contains(ApiServiceSelection.Setting, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The host's builder for every service the API runs, with <paramref name="settings"/> as a JSON source.</summary>
    private static WebApplicationBuilder Builder(string settings) => AvalonApiHost.CreateBuilder(
        new WebApplicationOptions { EnvironmentName = Environments.Production }, ApiServices.All, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Application:Authentication:IssuerSigningKey"] = new string('k', 64),
            });
            builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings)));
        });
}
