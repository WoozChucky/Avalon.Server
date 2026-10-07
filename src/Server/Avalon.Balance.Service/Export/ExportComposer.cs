using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Mapping;

namespace Avalon.Balance.Service.Export;

/// <summary>Turns a set of tweaks into a branch, its changed files and a draft pull request.</summary>
public static partial class ExportComposer
{
    public const string PlayerNote = "Player note: No gameplay changes: balance tuning proposal.";
    public const int MaxBranchAttempts = 50;
    public const int MaxTitleLength = 100;
    private const int MaxSlugLength = 40;
    private const int WorstMetrics = 10;

    private static readonly JsonSerializerOptions s_overrideFormat = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<ExportResultDto> ComposeAsync(
        ExportRequestDto request,
        BalanceHost host,
        IGitHub github,
        TimeProvider time,
        string commit,
        RunResultDto? run,
        CancellationToken ct)
    {
        string title = (request.Title ?? "").Trim();
        var issues = new List<IssueDto>();
        if (TitleIssue(title) is { } titleIssue)
            issues.Add(titleIssue);

        Dictionary<string, JsonElement> given = request.Overrides ?? [];
        OverrideReport report = ApplyOverrides(host, given, issues);

        BalanceConfig config = host.Defaults;
        if (request.Config is { } configDto)
        {
            (BalanceConfig? parsed, IReadOnlyList<IssueDto> configIssues) = WireMapping.ToConfig(configDto);
            issues.AddRange(configIssues);
            config = parsed ?? config;
        }

        if (issues.Count > 0)
            throw new ExportInvalidException(issues);

        // An override equal to the seed changes nothing; leaving it out keeps the file honest.
        var stale = new HashSet<string>(report.Stale, StringComparer.Ordinal);
        (string scenarios, string targets, string rotations) = ConfigFiles.Save(config);
        var candidates = new (string Path, string Text)[]
        {
            ("balance/overrides.json", OverridesText(given, stale)),
            ("balance/scenarios.json", scenarios),
            ("balance/targets.json", targets),
            ("balance/rotations.json", rotations),
        };

        var changed = new List<(string Path, string Text, string? Sha)>();
        foreach ((string path, string text) in candidates)
        {
            (string Sha, string Text)? existing = await Step("read " + path, ct, () => github.GetFileAsync(path, commit, ct));
            if (existing is null || !string.Equals(existing.Value.Text, text, StringComparison.Ordinal))
                changed.Add((path, text, existing?.Sha));
        }

        if (changed.Count == 0)
            throw new ExportInvalidException([new IssueDto("export", "nothing to export: every file matches the service's commit")]);

        string branch = await PickBranchAsync(github, Slug(title), time.GetUtcNow(), ct);
        string prTitle = "chore(balance): " + title.TrimEnd('.', ' ');

        await StepVoid("create branch", ct, () => github.CreateBranchAsync(branch, commit, ct));
        foreach ((string path, string text, string? sha) in changed)
            await StepVoid("commit " + path, ct, () => github.PutFileAsync(branch, path, text, sha, prTitle, ct));

        string body = Body(request.Notes, report, changed.Select(c => c.Path), run);
        string url = await Step("open pull request", ct, () => github.OpenDraftPullRequestAsync(branch, prTitle, body, ct));
        return new ExportResultDto(url, branch);
    }

    private static OverrideReport ApplyOverrides(BalanceHost host, Dictionary<string, JsonElement> given, List<IssueDto> issues)
    {
        try
        {
            return Overrides.Apply(host.Seed.Clone(), JsonSerializer.SerializeToElement(given));
        }
        catch (InvalidDataException e)
        {
            Match key = OverrideKey().Match(e.Message);
            issues.Add(new IssueDto(key.Success ? $"overrides.{key.Groups["key"].Value}" : "overrides", e.Message));
            return OverrideReport.None;
        }
    }

    private static IssueDto? TitleIssue(string title)
    {
        if (title.Length == 0)
            return new IssueDto("title", "title is required");
        if (title.Length > MaxTitleLength)
            return new IssueDto("title", $"title must be {MaxTitleLength} characters or fewer");
        // Any control or Unicode line/paragraph separator (U+2028, U+2029, U+0085, CR, LF) would split the commit message.
        return title.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            ? new IssueDto("title", "title must be one line")
            : null;
    }

    /// <summary>Flat JSON, sorted by key, indented by 2, ending with a newline; an empty map is <c>{}</c>.</summary>
    private static string OverridesText(Dictionary<string, JsonElement> given, HashSet<string> stale)
    {
        var sorted = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string key, JsonElement value) in given)
        {
            if (!stale.Contains(key))
                sorted.Add(key, value);
        }

        return JsonSerializer.Serialize(sorted, s_overrideFormat) + "\n";
    }

    public static string Slug(string title)
    {
        string slug = NonSlug().Replace(title.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > MaxSlugLength)
            slug = slug[..MaxSlugLength].TrimEnd('-');
        return slug.Length == 0 ? "tuning" : slug;
    }

    private static async Task<string> PickBranchAsync(IGitHub github, string slug, DateTimeOffset now, CancellationToken ct)
    {
        string baseName = $"balance/{slug}-{now.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}";
        for (int attempt = 1; attempt <= MaxBranchAttempts; attempt++)
        {
            string candidate = attempt == 1 ? baseName : $"{baseName}-{attempt}";
            if (!await Step("check branch " + candidate, ct, () => github.BranchExistsAsync(candidate, ct)))
                return candidate;
        }

        throw new ExportFailedException($"GitHub export failed at step 'pick a free branch name': {MaxBranchAttempts} names were taken");
    }

    private static string Body(string? notes, OverrideReport report, IEnumerable<string> files, RunResultDto? run)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(notes))
            sb.Append(DefuseNotes(notes.Trim())).Append("\n\n");

        if (report.Applied.Count > 0)
        {
            sb.Append("## Value tweaks\n\n| Key | Seed | New |\n| --- | --- | --- |\n");
            foreach (AppliedOverride a in report.Applied.OrderBy(a => a.Key, StringComparer.Ordinal))
                sb.Append("| ").Append(Cell(a.Key)).Append(" | ").Append(Cell(a.Seed)).Append(" | ").Append(Cell(a.Value)).Append(" |\n");
            sb.Append('\n');
        }

        sb.Append("## Files changed\n\n");
        foreach (string file in files)
            sb.Append("- `").Append(file).Append("`\n");
        sb.Append('\n');

        if (run is not null)
        {
            sb.Append("## Simulation\n\n")
                .Append(CultureInfo.InvariantCulture, $"Summary: green {run.Summary.Green}, yellow {run.Summary.Yellow}, red {run.Summary.Red}\n\n");
            MetricDto[] worst = run.Metrics
                .Where(m => !string.Equals(m.Grade, "Green", StringComparison.Ordinal))
                .OrderByDescending(m => string.Equals(m.Grade, "Red", StringComparison.Ordinal) ? 2 : 1)
                .ThenByDescending(Severity)
                .Take(WorstMetrics)
                .ToArray();
            if (worst.Length > 0)
            {
                sb.Append("Worst metrics:\n\n| Metric | Row | Value | Band | Grade |\n| --- | --- | --- | --- | --- |\n");
                foreach (MetricDto m in worst)
                {
                    sb.Append("| ").Append(Cell(m.Metric)).Append(" | ").Append(Cell(m.RowId ?? "")).Append(" | ")
                        .Append(Cell(Number(m.Value))).Append(" | ")
                        .Append(Cell($"{Number(m.Band.Min)} to {Number(m.Band.Max)} {m.Unit}")).Append(" | ")
                        .Append(Cell(m.Grade)).Append(" |\n");
                }

                sb.Append('\n');
            }
        }

        sb.Append(PlayerNote).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// The release-notes parser takes the first line matching <c>player note:</c> (case-insensitive, after optional whitespace or
    /// <c>&gt;*-</c>), so user notes must not contain one. A backslash before the colon breaks the match for every spelling and
    /// position (the parser needs the literal text <c>note:</c>), and Markdown renders a backslash-colon as a plain colon.
    /// It is applied anywhere in the text, not only at line starts, so odd line separators cannot hide a match.
    /// </summary>
    internal static string DefuseNotes(string notes) => PlayerNotePhrase().Replace(notes, "${phrase}\\:");

    /// <summary>How far out, relative to the edge crossed; a missing value counts as the farthest.</summary>
    private static double Severity(MetricDto m)
    {
        if (m.Value is not { } v)
            return double.MaxValue;
        double distance = m.Band.Min is { } lo && v < lo ? lo - v : m.Band.Max is { } hi && v > hi ? v - hi : 0d;
        double edge = m.Band.Min is { } lo2 && v < lo2 ? lo2 : m.Band.Max ?? 1d;
        return distance / Math.Max(1d, Math.Abs(edge));
    }

    private static string Number(double? value) => value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "-";

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ").Replace("\n", " ");

    private static async Task StepVoid(string step, CancellationToken ct, Func<Task> call) =>
        await Step(step, ct, async () =>
        {
            await call();
            return true;
        });

    /// <summary>Runs one GitHub call; a failure becomes an export failure naming the step and GitHub's status, nothing else.</summary>
    private static async Task<T> Step<T>(string step, CancellationToken ct, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (GitHubApiException e)
        {
            string status = e.StatusCode is { } s ? $" (HTTP {s})" : "";
            throw new ExportFailedException($"GitHub refused to {step}{status}", e);
        }
        catch (HttpRequestException e)
        {
            throw new ExportFailedException($"GitHub could not be reached to {step}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            // The HttpClient's own timeout; a caller's cancellation is not a TaskCanceledException from here.
            throw new ExportFailedException($"GitHub timed out while trying to {step}", e);
        }
    }

    [GeneratedRegex(@"(?<phrase>player\s+note)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlayerNotePhrase();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NonSlug();

    [GeneratedRegex(@"^Override '(?<key>[^']+)'", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OverrideKey();
}
