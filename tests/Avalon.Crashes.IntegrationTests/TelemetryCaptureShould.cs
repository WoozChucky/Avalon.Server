using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Avalon.Crashes.IntegrationTests;

public sealed class TelemetryCaptureShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_detect_escaped_private_values_and_property_names(bool propertyName)
    {
        const string Canary = "[\"private\\signature\",\"line\nvalue\"]";
        var value = new Dictionary<string, object> { [propertyName ? Canary : "nested"] = new[] { "prefix" + Canary + "suffix" } };
        if (propertyName) value[Canary] = "safe";
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        Assert.Throws<DoesNotContainException>(() => TelemetryCapture.AssertPrivateValuesAbsent(document.RootElement, [Canary]));
        TelemetryCapture.AssertPrivateValuesAbsent(document.RootElement, ["absent-canary"]);
    }
}
