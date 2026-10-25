using System.Text.Json;
using Xunit;

namespace Avalon.Crashes.IntegrationTests;

internal static class TelemetryCapture
{
    public static async Task<(int Spans, int Logs, int Metrics)> AssertAsync(string service, Guid[] reportIds, string[] privateValues)
    {
        await Task.Delay(1500);
        string[] lines = await File.ReadAllLinesAsync("/capture/export.json");
        var metrics = new HashSet<string>(StringComparer.Ordinal);
        int spans = 0, logs = 0;
        var loggedReports = new HashSet<Guid>();
        var traces = new HashSet<string>(StringComparer.Ordinal);
        var reportTraces = new Dictionary<Guid, string>();
        foreach (string line in lines.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            using var document = JsonDocument.Parse(line);
            foreach (string name in new[] { "resourceSpans", "resourceLogs", "resourceMetrics" })
            {
                if (!document.RootElement.TryGetProperty(name, out JsonElement resources)) continue;
                foreach (JsonElement resource in resources.EnumerateArray())
                {
                    if (!resource.GetProperty("resource").GetProperty("attributes").EnumerateArray().Any(a =>
                        a.GetProperty("key").GetString() == "service.name" && a.GetProperty("value").GetProperty("stringValue").GetString() == service))
                    {
                        continue;
                    }

                    AssertPrivateValuesAbsent(resource, [.. privateValues, "http.url", "url.full", "db.statement", "db.query.text", "garage-tls", "Database=", "Username=", "signature="]);
                    string scopeName = name switch { "resourceSpans" => "scopeSpans", "resourceLogs" => "scopeLogs", _ => "scopeMetrics" };
                    foreach (JsonElement scope in resource.GetProperty(scopeName).EnumerateArray())
                    {
                        if (name == "resourceSpans")
                        {
                            foreach (JsonElement span in scope.GetProperty("spans").EnumerateArray())
                            {
                                Assert.Equal("process", span.GetProperty("name").GetString());
                                traces.Add(span.GetProperty("traceId").GetString()!);
                                spans++;
                            }
                        }
                        else if (name == "resourceLogs")
                        {
                            foreach (JsonElement log in scope.GetProperty("logRecords").EnumerateArray())
                            {
                                Assert.Equal("Crash worker event.", log.GetProperty("body").GetProperty("stringValue").GetString());
                                foreach (JsonElement attribute in log.GetProperty("attributes").EnumerateArray())
                                {
                                    if (attribute.GetProperty("key").GetString() == "report_id")
                                    {
                                        var reportId = Guid.Parse(attribute.GetProperty("value").GetProperty("stringValue").GetString()!);
                                        loggedReports.Add(reportId);
                                        reportTraces[reportId] = log.GetProperty("traceId").GetString()!;
                                    }
                                }
                                logs++;
                            }
                        }
                        else
                        {
                            foreach (JsonElement metric in scope.GetProperty("metrics").EnumerateArray())
                            {
                                string metricName = metric.GetProperty("name").GetString()!;
                                Assert.StartsWith("avalon.crashes.", metricName);
                                metrics.Add(metricName);
                            }
                        }
                    }
                }
            }
        }
        Assert.True(spans >= 4, $"Expected report/fetch/tool/database spans, got {spans}.");
        Assert.Contains("avalon.crashes.reports", metrics);
        Assert.Contains("avalon.crashes.tool_failures", metrics);
        Assert.Contains("avalon.crashes.processing.duration", metrics);
        foreach (Guid reportId in reportIds)
        {
            Assert.Contains(reportId, loggedReports);
            Assert.Contains(reportTraces[reportId], traces);
        }
        Assert.True(logs >= 4, $"Expected report-triggered finite events, got {logs}.");
        return (spans, logs, metrics.Count);
    }

    internal static void AssertPrivateValuesAbsent(JsonElement element, string[] privateValues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    foreach (string secret in privateValues) Assert.DoesNotContain(secret, property.Name);
                    AssertPrivateValuesAbsent(property.Value, privateValues);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray()) AssertPrivateValuesAbsent(item, privateValues);
                break;
            case JsonValueKind.String:
                foreach (string secret in privateValues) Assert.DoesNotContain(secret, element.GetString()!);
                break;
        }
    }
}
