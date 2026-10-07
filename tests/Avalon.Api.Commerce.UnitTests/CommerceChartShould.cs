using Xunit;

namespace Avalon.Api.Commerce.UnitTests;

public sealed class CommerceChartShould
{
    private static string Root
    {
        get { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Server", "Avalon.Api", "Helm"))) dir = dir.Parent; return dir?.FullName ?? throw new InvalidOperationException("Repository root not found."); }
    }
    [Fact]
    public void Default_chart_disables_checkout()
    {
        string values = File.ReadAllText(Path.Combine(Root, "src/Server/Avalon.Api/Helm/avalon-api/values.yaml"));
        Assert.Matches("(?m)^commerce:\\r?\\n\\s+enabled: false", values);
    }
    [Fact]
    public void Keys_are_only_secret_references()
    {
        string deployment = File.ReadAllText(Path.Combine(Root, "src/Server/Avalon.Api/Helm/avalon-api/templates/deployment.yaml"));
        Assert.Matches("(?s)name: Application__Commerce__ApiKey\\s+valueFrom:\\s+secretKeyRef:", deployment);
        Assert.Matches("(?s)name: Application__Commerce__WebhookSecret\\s+valueFrom:\\s+secretKeyRef:", deployment);
        Assert.DoesNotContain(".Values.commerce.apiKey |", deployment);
    }
    [Fact]
    public void Chart_refuses_inline_credentials_and_production_or_live_enablement()
    {
        string helper = File.ReadAllText(Path.Combine(Root, "src/Server/Avalon.Api/Helm/avalon-api/templates/_helpers.tpl"));
        Assert.Contains("avalon-api.validateCommerce", helper);
        Assert.Contains("commerce.apiKey is forbidden", helper);
        Assert.Contains("commerce.webhookSecret is forbidden", helper);
        Assert.Contains("commerce requires an isolated Development sandbox", helper);
    }
}
