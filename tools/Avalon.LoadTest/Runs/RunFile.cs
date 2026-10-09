using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.LoadTest.Runs;

/// <summary>
/// A provisioned run of bot accounts, kept in <see cref="Directory"/> as <c>&lt;RunId&gt;.json</c> until
/// <c>cleanup</c> deletes its accounts. The only place the bots' password is kept: the file is the user's own (owner-only
/// on Unix), and nothing prints it.
/// </summary>
/// <param name="RunId">The first run's id: the file's name and the handle <c>--run</c> takes.</param>
/// <param name="RunIds">
/// Every run the API created for it, <paramref name="RunId"/> first: the API makes at most 1,000 accounts per run and
/// never reuses a run id, so a larger count is several runs.
/// </param>
/// <param name="Api">The API origin the accounts were made on, with its trailing slash.</param>
/// <param name="WorldId">The only world the run's bots enter.</param>
/// <param name="BotPassword">The password every account of the run signs in with.</param>
/// <param name="Bots">The accounts' usernames, run by run, in index order.</param>
public sealed record RunFile(
    string RunId, IReadOnlyList<string> RunIds, Uri Api, ushort WorldId, string BotPassword, IReadOnlyList<string> Bots,
    DateTimeOffset CreatedAt)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary><c>%LOCALAPPDATA%\Avalon.LoadTest\runs</c> (on Unix, <c>~/.local/share/Avalon.LoadTest/runs</c>).</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avalon.LoadTest", "runs");

    /// <summary>Where this run is kept.</summary>
    [JsonIgnore]
    public string FilePath => PathOf(RunId);

    /// <summary>
    /// The run <paramref name="runId"/> names (any case), or with null the only run there is. No run, an unknown one, or
    /// several with none named is a <see cref="CommandLineException"/> listing the runs kept.
    /// </summary>
    public static RunFile Load(string? runId)
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
            return run.RunId == chosen && run.RunIds is [_, ..] && run.Bots is not null
                ? run
                : throw new CommandLineException($"{path} is not a run file of run {chosen}.");
        }
        catch (JsonException error)
        {
            throw new CommandLineException($"{path} cannot be read as a run file: {error.Message}");
        }
    }

    /// <summary>Writes the run, replacing what was kept for it, through a temporary file so no half-written file is left.</summary>
    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = FilePath;
        string temporary = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using (var stream = new FileStream(temporary, options))
            JsonSerializer.Serialize(stream, this, s_json);

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Forgets the run: its accounts are gone.</summary>
    public void Delete() => File.Delete(FilePath);

    private static string PathOf(string runId) => Path.Combine(Directory, runId + ".json");

    private static string[] Kept() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json")
                .Select(file => Path.GetFileNameWithoutExtension(file))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
}
