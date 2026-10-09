using System.Security.Cryptography;
using Avalon.LoadTest.Api;

namespace Avalon.LoadTest.Runs;

/// <summary>
/// <c>provision</c> and <c>cleanup</c>: an admin creates a run of bot accounts through
/// <c>POST /admin/load-test/accounts</c>, kept in a <see cref="RunFile"/>, and deletes it through
/// <c>DELETE /admin/load-test/accounts?run=</c>. Neither prints a password.
/// </summary>
public static class RunCommands
{
    /// <summary>The most accounts the API makes in one run; a larger count is several runs, each with its own id.</summary>
    private const int MaxRunSize = 1000;

    private const string BotPasswordCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>Making 1,000 accounts and deleting them with their characters in every world takes a while.</summary>
    private static readonly TimeSpan s_adminTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Creates <see cref="ProvisionOptions.Count"/> accounts in runs of at most 1,000, all with one random bot password,
    /// and keeps them in one run file, saved after each run so a failure part way still leaves what was made deletable.
    /// </summary>
    public static async Task<int> ProvisionAsync(ProvisionOptions options, CancellationToken ct)
    {
        using var api = new ApiClient(options.Api, s_adminTimeout);
        AdminLogin admin = await AdminLogin.SignInAsync(api, ct);
        string botPassword = RandomNumberGenerator.GetString(BotPasswordCharacters, 24);

        RunFile? run = null;
        try
        {
            for (int remaining = options.Count; remaining > 0;)
            {
                int size = Math.Min(remaining, MaxRunSize);
                (string runId, IReadOnlyList<string> accounts) = await api.CreateRunAsync(admin.Token,
                    run is null ? options.RunId : null, size, botPassword, admin.Password, ct);
                run = run is null
                    ? new RunFile(runId, [runId], options.Api, options.World, botPassword, accounts, DateTimeOffset.UtcNow)
                    : run with { RunIds = [.. run.RunIds, runId], Bots = [.. run.Bots, .. accounts] };
                run.Save();
                remaining -= size;
            }
        }
        catch (Exception error) when (run is not null && error is ApiException or OperationCanceledException)
        {
            Console.Error.WriteLine($"Run {run.RunId}: {run.Bots.Count} of {options.Count} accounts made before " +
                                    $"{(error is ApiException ? error.Message : "the cancel")}. Kept in {run.FilePath}; " +
                                    $"cleanup --run {run.RunId} deletes them.");
            return 1;
        }

        Console.WriteLine($"Run {run!.RunId}: {run.Bots.Count} accounts for world {run.WorldId} on {run.Api}" +
                          (run.RunIds.Count > 1 ? $" (runs {string.Join(", ", run.RunIds)})." : "."));
        Console.WriteLine($"Kept in {run.FilePath}");
        return 0;
    }

    /// <summary>
    /// Deletes every run in the run file, going on past a run that fails, and forgets the file only when all succeeded.
    /// </summary>
    public static async Task<int> CleanupAsync(CleanupOptions options, CancellationToken ct)
    {
        var run = RunFile.Load(options.RunId);
        using var api = new ApiClient(run.Api, s_adminTimeout);
        AdminLogin admin = await AdminLogin.SignInAsync(api, ct);

        bool all = true;
        foreach (string runId in run.RunIds)
        {
            try
            {
                (int deleted, IReadOnlyList<string> skipped) = await api.DeleteRunAsync(admin.Token, runId, admin.Password, ct);
                Console.WriteLine(skipped.Count == 0
                    ? $"Run {runId}: {deleted} accounts deleted."
                    : $"Run {runId}: {deleted} accounts deleted; kept, as they hold more than a load test gives: " +
                      string.Join(", ", skipped));
            }
            catch (ApiException error) when (error.Status == 409)
            {
                all = false;
                Console.Error.WriteLine($"Run {runId}: bots still hold live sessions: stop the ramp and wait about a " +
                                        "minute, then retry.");
            }
            catch (ApiException error)
            {
                all = false;
                Console.Error.WriteLine($"Run {runId}: {error.Message}");
            }
        }

        if (!all)
        {
            Console.Error.WriteLine($"{run.FilePath} is kept: run cleanup again to delete what is left.");
            return 1;
        }

        run.Delete();
        Console.WriteLine($"Run {run.RunId} is gone; {run.FilePath} deleted.");
        return 0;
    }
}
