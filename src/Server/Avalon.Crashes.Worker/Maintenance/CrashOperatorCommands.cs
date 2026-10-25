using System.Globalization;
using System.Text.Json;
using Avalon.Crashes.Persistence;

namespace Avalon.Crashes.Worker.Maintenance;

public sealed class CrashOperatorCommands(ICrashMaintenanceStore store, CrashRetentionService retention, TimeProvider time)
{
    public async Task<int> ExecuteAsync(string[] args, TextWriter output, CancellationToken ct)
    {
        if (args is ["cleanup"])
        {
            long removed = await retention.SweepAsync(ct);
            await output.WriteLineAsync($"Deleted {removed.ToString(CultureInfo.InvariantCulture)} expired reports.");
            return 0;
        }
        if (args is ["retry", "--report-id", string value] && Guid.TryParse(value, out Guid id))
        {
            RetryResult result = await store.RetryFailedAsync(new CrashReportId(id), time.GetUtcNow(), ct);
            await output.WriteLineAsync(result == RetryResult.Retried ? "Retried." : "Not eligible.");
            return result == RetryResult.Retried ? 0 : 1;
        }
        if (args.Length > 0 && args[0] == "list-failures")
        {
            int page = 1;
            int size = 50;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !seen.Add(args[i]) || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    return 2;
                if (args[i] == "--page") page = number;
                else if (args[i] == "--page-size") size = number;
                else return 2;
            }
            if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
                return 2;
            FailurePage result = await store.ListFailuresAsync(page, size, ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        await output.WriteLineAsync("Invalid command.");
        return 2;
    }
}
