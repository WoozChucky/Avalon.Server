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

    /// <summary>The run ids tried for one run before giving up, when the ones picked are already used.</summary>
    private const int MaxRunIdAttempts = 5;

    private const string RunIdLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private const string BotPasswordCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>Making 1,000 accounts and deleting them with their characters in every world takes a while.</summary>
    private static readonly TimeSpan s_adminTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Creates <see cref="ProvisionOptions.Count"/> accounts in runs of at most 1,000, all with one random bot password,
    /// and keeps them in one run file. The tool picks each run's id and saves it, with the bot password, before asking
    /// for the run: a create whose reply is lost (a timeout, a cancel) after the API committed still leaves its id in the
    /// file for <c>cleanup</c>.
    /// </summary>
    public static async Task<int> ProvisionAsync(ProvisionOptions options, CancellationToken ct)
    {
        if (options.RunId is { } named && RunFile.Exists(named))
            throw new CommandLineException($"Run {named} is already kept in {RunFile.Directory}: clean it up or name another.");

        using var api = new ApiClient(options.Api, s_adminTimeout);
        AdminLogin admin = await AdminLogin.SignInAsync(api, ct);
        string botPassword = RandomNumberGenerator.GetString(BotPasswordCharacters, 24);

        RunFile? run = null;
        try
        {
            for (int remaining = options.Count; remaining > 0;)
            {
                int size = Math.Min(remaining, MaxRunSize);
                IReadOnlyList<string> accounts;
                for (int attempt = 1; ; attempt++)
                {
                    string runId = run is null && options.RunId is not null ? options.RunId : NewRunId(run);
                    if (run is null)
                    {
                        run = new RunFile(runId, [runId], options.Api, options.World, botPassword, [], DateTimeOffset.UtcNow);
                        run.Save();
                        Console.Error.WriteLine($"Run file: {run.FilePath}");
                    }
                    else
                    {
                        run = run with { RunIds = [.. run.RunIds, runId] };
                        run.Save();
                    }

                    try
                    {
                        (string created, accounts) = await api.CreateRunAsync(admin.Token, runId, size, botPassword,
                            admin.Password, ct);
                        if (created != runId)
                        {
                            run = run with { RunIds = [.. run.RunIds, created] };
                            run.Save();
                            throw new ApiException("create-run", 201, $"run {created} was made instead of run {runId}");
                        }
                        break;
                    }
                    catch (ApiException error) when (IsRunIdTaken(error))
                    {
                        // Another run's id: it must leave the file, or cleanup would delete that run.
                        run = Forget(run, runId);
                        // A run id the user named is not swapped for another; a picked one is, a few times.
                        if ((options.RunId is not null && run is null) || attempt == MaxRunIdAttempts)
                            throw;
                    }
                }

                run = run! with { Bots = [.. run.Bots, .. accounts] };
                run.Save();
                remaining -= size;
            }
        }
        catch (Exception error) when (run is not null && error is ApiException or OperationCanceledException)
        {
            Console.Error.WriteLine($"Run {run.RunId}: {run.Bots.Count} of {options.Count} accounts confirmed before " +
                                    $"{(error is ApiException ? error.Message : "the cancel")}. Every run id asked for is " +
                                    $"kept in {run.FilePath}; cleanup --run {run.RunId} deletes them.");
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
                                        $"minute, then retry. The API said: {error.Detail}");
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

    /// <summary>A run id no run of <paramref name="run"/> has and no kept run file is named after: three letters A-Z.</summary>
    private static string NewRunId(RunFile? run)
    {
        while (true)
        {
            string runId = RandomNumberGenerator.GetString(RunIdLetters, 3);
            if (run?.RunIds.Contains(runId, StringComparer.Ordinal) != true && !RunFile.Exists(runId))
                return runId;
        }
    }

    /// <summary>The API's 409 for a run id already used, or taken by another create while this one ran.</summary>
    private static bool IsRunIdTaken(ApiException error) =>
        error.Status == 409 && (error.Detail.Contains("already used", StringComparison.Ordinal)
                                || error.Detail.Contains("was taken", StringComparison.Ordinal));

    /// <summary>
    /// <paramref name="run"/> without <paramref name="runId"/>, its last id, saved; null, with the file deleted, when it
    /// was the only one.
    /// </summary>
    private static RunFile? Forget(RunFile run, string runId)
    {
        if (run.RunIds.Count == 1)
        {
            run.Delete();
            return null;
        }

        RunFile kept = run with { RunIds = [.. run.RunIds.Where(id => id != runId)] };
        kept.Save();
        return kept;
    }
}
