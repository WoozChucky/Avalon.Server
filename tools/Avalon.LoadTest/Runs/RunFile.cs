using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.LoadTest.Runs;

/// <summary>Where a run id of a <see cref="RunFile"/> stands.</summary>
public enum RunStatus
{
    /// <summary>
    /// Saved before its create call, which has not answered: the API may have made the run (a reply lost to a timeout or
    /// a cancel), or not, in which case another provision may since have taken the id.
    /// </summary>
    Pending,

    /// <summary>The API answered the create call with this id: the run is this file's.</summary>
    Created,
}

/// <summary>One run id a <see cref="RunFile"/> asked the API for, and where it stands.</summary>
public sealed record RunEntry(string Id, RunStatus Status);

/// <summary>Another kept run file, as <c>cleanup</c> checks a pending run id against it.</summary>
/// <param name="RunId">The file's run.</param>
/// <param name="Ids">The run ids it lists; null when it cannot be read, so it may list any.</param>
public sealed record KeptRun(string RunId, IReadOnlyList<string>? Ids);

/// <summary>
/// A provisioned run of bot accounts, kept in <see cref="Directory"/> as <c>&lt;RunId&gt;.json</c> until
/// <c>cleanup</c> deletes its accounts. The only place the bots' password is kept: the file is the user's own (owner-only
/// on Unix), and nothing prints it.
/// </summary>
/// <param name="RunId">The first run's id: the file's name and the handle <c>--run</c> takes.</param>
/// <param name="Runs">
/// Every run <c>provision</c> asked the API for, <paramref name="RunId"/> first: the API makes at most 1,000 accounts
/// per run and never reuses a run id, so a larger count is several runs. Each id is saved here as
/// <see cref="RunStatus.Pending"/> before its create call and becomes <see cref="RunStatus.Created"/> once the call
/// answers, so one whose reply never came (a timeout, a cancel) is still known to <c>cleanup</c>; it may hold no account.
/// </param>
/// <param name="Api">The API origin the accounts were made on, with its trailing slash.</param>
/// <param name="WorldId">The only world the run's bots enter.</param>
/// <param name="BotPassword">The password every account of the run signs in with.</param>
/// <param name="Bots">The accounts' usernames, run by run, in index order.</param>
public sealed record RunFile(
    string RunId, IReadOnlyList<RunEntry> Runs, Uri Api, ushort WorldId, string BotPassword, IReadOnlyList<string> Bots,
    DateTimeOffset CreatedAt)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<RunStatus>() },
    };

    /// <summary><c>%LOCALAPPDATA%\Avalon.LoadTest\runs</c> (on Linux <c>~/.local/share/Avalon.LoadTest/runs</c>, on macOS <c>~/Library/Application Support/Avalon.LoadTest/runs</c>).</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avalon.LoadTest", "runs");

    /// <summary>Where this run is kept.</summary>
    [JsonIgnore]
    public string FilePath => PathOf(RunId);

    /// <summary>
    /// The run <paramref name="runId"/> names (any case), or with null the only run there is. No run, an unknown one,
    /// several with none named, a file that cannot be read, or one that is not a run file is a
    /// <see cref="CommandLineException"/> saying which.
    /// </summary>
    /// <param name="forBots">
    /// The run's bots are to play (<c>check</c>, <c>ramp</c>): a run that lists no bot (a provision that saved none),
    /// has no bot password or names world 0 is refused too. <c>cleanup</c> needs none of that and deletes such a run.
    /// </param>
    public static RunFile Load(string? runId, bool forBots)
    {
        try
        {
            RunFile run = LoadKept(runId);
            if (!forBots) return run;

            if (run.Bots.Count == 0)
            {
                throw new CommandLineException(
                    $"Run {run.RunId} lists no bots (its provision saved none): run cleanup --run {run.RunId}, then provision again.");
            }

            if (string.IsNullOrEmpty(run.BotPassword))
                throw new CommandLineException($"{run.FilePath} has no bot password: the bots cannot sign in.");

            return run.WorldId != 0
                ? run
                : throw new CommandLineException($"{run.FilePath} names no world (worldId 0): world ids start at 1.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new CommandLineException($"The run files in {Directory} cannot be read: {error.Message}");
        }
    }

    private static RunFile LoadKept(string? runId)
    {
        string[] kept = Kept();
        string chosen;
        if (runId is null)
        {
            chosen = kept switch
            {
                [] => throw new CommandLineException($"No run is kept in {Directory}: provision one first."),
                [var only] => only,
                _ => throw new CommandLineException($"Several runs are kept ({string.Join(", ", kept)}): name one with --run."),
            };
        }
        else
        {
            chosen = runId.ToUpperInvariant();
            if (!kept.Contains(chosen, StringComparer.Ordinal))
            {
                throw new CommandLineException(kept.Length == 0
                    ? $"Run {chosen} is not kept, and no run is, in {Directory}."
                    : $"Run {chosen} is not kept in {Directory}; the runs kept are {string.Join(", ", kept)}.");
            }
        }

        string path = PathOf(chosen);
        try
        {
            RunFile run = JsonSerializer.Deserialize<RunFile>(File.ReadAllText(path), s_json)
                ?? throw new CommandLineException($"{path} is empty.");
            if (run.RunId != chosen || run.Runs is not [var first, ..] || first?.Id != chosen || run.Bots is null ||
                run.Api is null || run.Runs.Any(entry => entry?.Id is not { Length: > 0 } || !Enum.IsDefined(entry.Status)) ||
                run.Bots.Any(bot => string.IsNullOrEmpty(bot)))
            {
                throw new CommandLineException($"{path} is not a run file of run {chosen}.");
            }

            // The admin's password goes to this origin: never in the clear, whatever the file was edited to.
            return run.Api.IsAbsoluteUri && run.Api.Scheme == Uri.UriSchemeHttps
                ? run
                : throw new CommandLineException($"{path} names an API that is not https: {run.Api}.");
        }
        catch (JsonException error)
        {
            throw new CommandLineException($"{path} cannot be read as a run file: {error.Message}");
        }
    }

    /// <summary>Whether a run file named <paramref name="runId"/> is kept.</summary>
    public static bool Exists(string runId) => File.Exists(PathOf(runId.ToUpperInvariant()));

    /// <summary>The run with <paramref name="id"/> at <paramref name="status"/>: updated where it is listed, else added last.</summary>
    public RunFile With(string id, RunStatus status) => this with
    {
        Runs = Runs.Any(entry => entry.Id == id)
            ? [.. Runs.Select(entry => entry.Id == id ? entry with { Status = status } : entry)]
            : [.. Runs, new RunEntry(id, status)],
    };

    /// <summary>The run without <paramref name="id"/>.</summary>
    public RunFile Without(string id) => this with { Runs = [.. Runs.Where(entry => entry.Id != id)] };

    /// <summary>The ids at <paramref name="status"/>, in the order asked.</summary>
    public IReadOnlyList<string> IdsAt(RunStatus status) =>
        [.. Runs.Where(entry => entry.Status == status).Select(entry => entry.Id)];

    /// <summary>
    /// Writes the run, replacing what was kept for it, through a temporary file so no half-written file is left.
    /// </summary>
    /// <exception cref="RunFileWriteException">The file could not be written; what was kept before is unchanged.</exception>
    public void Save()
    {
        string path = FilePath;
        string temporary = path + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var stream = new FileStream(temporary, options))
                JsonSerializer.Serialize(stream, this, s_json);

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new RunFileWriteException(path, "written", error);
        }
    }

    /// <summary>Forgets the run: its accounts are gone, or it holds no run of this provision.</summary>
    /// <exception cref="RunFileWriteException">The file could not be deleted.</exception>
    public void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new RunFileWriteException(FilePath, "deleted", error);
        }
    }

    /// <summary>
    /// Every kept run file but <paramref name="exceptRunId"/>'s, with the run ids it lists; a file that cannot be read
    /// is listed with none known (<see cref="KeptRun.Ids"/> null).
    /// </summary>
    public static IReadOnlyList<KeptRun> Others(string? exceptRunId) =>
        [.. Kept().Where(runId => runId != exceptRunId).Select(runId => new KeptRun(runId, IdsListed(PathOf(runId))))];

    /// <summary>The record's text without the bots' password.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RunId = ").Append(RunId)
            .Append(", Runs = [").AppendJoin(", ", Runs.Select(entry => $"{entry.Id} {entry.Status}"))
            .Append("], Api = ").Append(Api)
            .Append(", WorldId = ").Append(WorldId)
            .Append(", BotPassword = (redacted), Bots = ").Append(Bots.Count)
            .Append(", CreatedAt = ").Append(CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    private static string PathOf(string runId) => Path.Combine(Directory, runId + ".json");

    /// <summary>The run ids a kept file lists; null when it cannot be read as a run file.</summary>
    private static string[]? IdsListed(string path)
    {
        try
        {
            RunFile? run = JsonSerializer.Deserialize<RunFile>(File.ReadAllText(path), s_json);
            return run?.Runs is { } runs && runs.All(entry => entry?.Id is not null)
                ? [.. runs.Select(entry => entry.Id)]
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string[] Kept() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json")
                .Select(file => Path.GetFileNameWithoutExtension(file))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
}

/// <summary>A run file could not be written or deleted; the message says which and why.</summary>
public sealed class RunFileWriteException(string path, string what, Exception inner)
    : Exception($"The run file {path} could not be {what}: {inner.Message}", inner);
