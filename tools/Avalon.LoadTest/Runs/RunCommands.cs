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
    /// and keeps them in one run file. The tool picks each run's id and saves it as <see cref="RunStatus.Pending"/>,
    /// with the bot password, before asking for the run, and as <see cref="RunStatus.Created"/> once the API answered: a
    /// create whose reply is lost (a timeout, a cancel) after the API committed still leaves its id in the file for
    /// <c>cleanup</c>. When the run file cannot be written, what was created is printed with how to delete it.
    /// </summary>
    public static async Task<int> ProvisionAsync(ProvisionOptions options, CancellationToken ct)
    {
        if (options.RunId is { } named && RunFile.Exists(named))
            throw new CommandLineException($"Run {named} is already kept in {RunFile.Directory}: clean it up or name another.");

        using var api = new ApiClient(options.Api, s_adminTimeout);
        AdminLogin admin = await AdminLogin.SignInAsync(api, ct);
        string botPassword = RandomNumberGenerator.GetString(BotPasswordCharacters, 24);

        // What the API was asked and answered, which the file on disk follows one save behind at most: an id is added
        // here only once saved as pending (its create call follows the save), every other change before its save.
        RunFile? run = null;
        string? filePath = null;
        try
        {
            for (int remaining = options.Count; remaining > 0;)
            {
                int size = Math.Min(remaining, MaxRunSize);
                string runId;
                IReadOnlyList<string> accounts;
                for (int attempt = 1; ; attempt++)
                {
                    runId = run is null && options.RunId is not null ? options.RunId : NewRunId(run);
                    RunFile asking = run is null
                        ? new RunFile(runId, [new RunEntry(runId, RunStatus.Pending)], options.Api, options.World,
                            botPassword, [], DateTimeOffset.UtcNow)
                        : run.With(runId, RunStatus.Pending);
                    filePath = asking.FilePath;
                    asking.Save();
                    if (run is null) Console.Error.WriteLine($"Run file: {asking.FilePath}");
                    run = asking;

                    try
                    {
                        (string created, accounts) = await api.CreateRunAsync(admin.Token, runId, size, botPassword,
                            admin.Password, ct);
                        if (created != runId)
                        {
                            run = run.With(created, RunStatus.Created);
                            run.Save();
                            throw new ApiException("create-run", 201, $"run {created} was made instead of run {runId}");
                        }
                        break;
                    }
                    catch (ApiException error) when (IsRunIdTaken(error))
                    {
                        // Another run's id: it must leave the file, or cleanup would delete that run. It leaves what
                        // this provision knows first, whatever then happens to the file; the file goes with its only id.
                        RunFile forgotten = run.Without(runId);
                        run = forgotten.Runs.Count == 0 ? null : forgotten;
                        if (run is null) forgotten.Delete();
                        else run.Save();
                        // A run id the user named is not swapped for another; a picked one is, a few times.
                        if ((options.RunId is not null && run is null) || attempt == MaxRunIdAttempts)
                            throw;
                    }
                }

                run = run!.With(runId, RunStatus.Created) with { Bots = [.. run.Bots, .. accounts] };
                run.Save();
                remaining -= size;
            }
        }
        catch (Exception error) when (run is not null && error is ApiException or OperationCanceledException)
        {
            Console.Error.WriteLine($"Run {run.RunId}: {run.Bots.Count} of {options.Count} accounts confirmed before " +
                                    $"{(error is ApiException ? error.Message : "the cancel")}. Every run id asked for is " +
                                    $"kept in {run.FilePath}{Statuses(run)}; cleanup --run {run.RunId} deletes them.");
            return 1;
        }
        catch (RunFileWriteException error)
        {
            Console.Error.WriteLine(AfterWriteFailure(error, run, filePath!));
            return 1;
        }

        Console.WriteLine($"Run {run!.RunId}: {run.Bots.Count} accounts for world {run.WorldId} on {run.Api}" +
                          (run.Runs.Count > 1 ? $" (runs {string.Join(", ", run.Runs.Select(entry => entry.Id))})." : "."));
        Console.WriteLine($"Kept in {run.FilePath}");
        return 0;
    }

    /// <summary>
    /// Deletes every run in the run file, going on past a run that fails, and forgets the file only when all succeeded.
    /// A <see cref="RunStatus.Created"/> run is deleted. A <see cref="RunStatus.Pending"/> one, whose create never
    /// answered, may be another provision's that took the id since: it is deleted only when no other kept run file lists
    /// it, and otherwise skipped and reported (that file's cleanup deletes it); with another file unreadable, it is
    /// skipped and this file kept.
    /// </summary>
    public static async Task<int> CleanupAsync(CleanupOptions options, CancellationToken ct)
    {
        var run = RunFile.Load(options.RunId);
        IReadOnlyList<KeptRun> others = RunFile.Others(run.RunId);
        using var api = new ApiClient(run.Api, s_adminTimeout);
        AdminLogin admin = await AdminLogin.SignInAsync(api, ct);

        bool all = true;
        foreach (RunEntry entry in run.Runs)
        {
            string runId = entry.Id;
            if (entry.Status == RunStatus.Pending)
            {
                string[] listing = [.. others.Where(other => other.Ids?.Contains(runId, StringComparer.Ordinal) == true)
                    .Select(other => other.RunId)];
                if (listing.Length > 0)
                {
                    Console.WriteLine($"Run {runId}: skipped. Its create never answered here, and the run file of " +
                                      $"{string.Join(", ", listing)} lists it too, so it may be that run's: its cleanup deletes it.");
                    continue;
                }

                string[] unreadable = [.. others.Where(other => other.Ids is null).Select(other => other.RunId)];
                if (unreadable.Length > 0)
                {
                    all = false;
                    Console.Error.WriteLine($"Run {runId}: skipped. Its create never answered here, and the run file of " +
                                            $"{string.Join(", ", unreadable)} cannot be read to tell whether it lists " +
                                            "it: repair or remove that file, then run cleanup again.");
                    continue;
                }
            }

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

        try
        {
            run.Delete();
        }
        catch (RunFileWriteException error)
        {
            Console.Error.WriteLine($"Run {run.RunId}: every run is deleted, but {error.Message}. Delete the file by " +
                                    "hand; a cleanup of it again would only find its runs already empty.");
            return 1;
        }

        Console.WriteLine($"Run {run.RunId} is gone; {run.FilePath} deleted.");
        return 0;
    }

    /// <summary>
    /// A run id no run of <paramref name="run"/> has, no kept run file is named after and no kept run file lists: three
    /// letters A-Z.
    /// </summary>
    private static string NewRunId(RunFile? run)
    {
        HashSet<string> listed = [.. RunFile.Others(run?.RunId).SelectMany(other => other.Ids ?? [])];
        while (true)
        {
            string runId = RandomNumberGenerator.GetString(RunIdLetters, 3);
            if (run?.Runs.Any(entry => entry.Id == runId) != true && !RunFile.Exists(runId) && !listed.Contains(runId))
                return runId;
        }
    }

    /// <summary>The API's 409 for a run id already used, or taken by another create while this one ran.</summary>
    private static bool IsRunIdTaken(ApiException error) =>
        error.Status == 409 && (error.Detail.Contains("already used", StringComparison.Ordinal)
                                || error.Detail.Contains("was taken", StringComparison.Ordinal));

    /// <summary>The run's ids by status, for a message: <c> (created ABC, DEF; pending GHI)</c>.</summary>
    private static string Statuses(RunFile run)
    {
        IReadOnlyList<string> created = run.IdsAt(RunStatus.Created);
        IReadOnlyList<string> pending = run.IdsAt(RunStatus.Pending);
        var parts = new List<string>();
        if (created.Count > 0) parts.Add($"created {string.Join(", ", created)}");
        if (pending.Count > 0) parts.Add($"pending {string.Join(", ", pending)}");
        return parts.Count == 0 ? "" : $" ({string.Join("; ", parts)})";
    }

    /// <summary>
    /// What a failed run file write left, and how to recover: the runs created (their accounts exist), those pending (a
    /// create that never answered, which may hold accounts), and how to delete them, as the file may lag one change
    /// behind them.
    /// </summary>
    private static string AfterWriteFailure(RunFileWriteException error, RunFile? run, string filePath)
    {
        if (run is null)
        {
            return $"{error.Message}. No account was created." + (File.Exists(filePath)
                ? $" {filePath} lists no run of this provision any more: delete it by hand rather than clean it up."
                : "");
        }

        IReadOnlyList<string> created = run.IdsAt(RunStatus.Created);
        IReadOnlyList<string> pending = run.IdsAt(RunStatus.Pending);
        string nl = Environment.NewLine;
        return $"{error.Message}.{nl}" +
               $"Run {run.RunId}: {run.Bots.Count} of the accounts asked for are confirmed." +
               (created.Count > 0 ? $"{nl}  Runs created (their accounts exist): {string.Join(", ", created)}." : "") +
               (pending.Count > 0 ? $"{nl}  Runs asked for with no answer (they may hold accounts): {string.Join(", ", pending)}." : "") +
               $"{nl}These are this provision's runs; {filePath} may be one change behind them. To recover, compare the " +
               $"file's runs with this list: remove from the file a run it lists that is not here (another provision's), " +
               $"then cleanup --run {run.RunId} deletes the runs the file lists; a run here the file does not list is " +
               $"deleted as an admin with DELETE {run.Api}admin/load-test/accounts?run=<id>.";
    }
}
