using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Export;
using Avalon.Balance.Service.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class ExportComposerShould
{
    private static readonly Lazy<BalanceHost> SharedHost = new(() =>
    {
        WebApplication app = BalanceTestHost.Build();
        return app.Services.GetRequiredService<BalanceHost>();
    });

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 14, 5, 59, TimeSpan.Zero);

    private static BalanceHost Host => SharedHost.Value;

    private static Dictionary<string, JsonElement> Overrides(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static ExportRequestDto Request(string title = "Slam tuning", string overrides = "{}", string? notes = null,
        BalanceConfigDto? config = null, string? runId = null) =>
        new(title, notes, Overrides(overrides), config, runId);

    /// <summary>A fake whose files at the commit are the checked-in defaults, so an empty export changes nothing.</summary>
    private static FakeGitHub GitHubWithDefaults()
    {
        var github = new FakeGitHub();
        (string scenarios, string targets, string rotations) = ConfigFiles.Save(Host.Defaults);
        github.FilesAtCommit["balance/overrides.json"] = ("sha-overrides", "{}\n");
        github.FilesAtCommit["balance/scenarios.json"] = ("sha-scenarios", scenarios);
        github.FilesAtCommit["balance/targets.json"] = ("sha-targets", targets);
        github.FilesAtCommit["balance/rotations.json"] = ("sha-rotations", rotations);
        return github;
    }

    private static Task<ExportResultDto> Compose(ExportRequestDto request, FakeGitHub github, RunResultDto? run = null,
        FakeTimeProvider? time = null) =>
        ExportComposer.ComposeAsync(request, Host, github, time ?? new FakeTimeProvider(Now), FakeGitHub.Commit, run, CancellationToken.None);

    [Fact]
    public async Task Put_only_the_files_that_changed()
    {
        FakeGitHub github = GitHubWithDefaults();

        ExportResultDto result = await Compose(Request(overrides: """{"Ability.201.EffectValue":18}"""), github);

        (string Branch, string Path, string Text, string? ExistingSha, string Message) put = Assert.Single(github.Puts);
        Assert.Equal("balance/overrides.json", put.Path);
        Assert.Equal("sha-overrides", put.ExistingSha);
        Assert.Equal("chore(balance): Slam tuning", put.Message);
        Assert.Equal(result.Branch, put.Branch);
        Assert.Equal(FakeGitHub.Commit, github.CreatedFrom);
        Assert.Equal("https://github.com/WoozChucky/Avalon.Server/pull/999", result.PullRequestUrl);
    }

    [Fact]
    public async Task Write_overrides_as_flat_sorted_indented_json_with_a_trailing_newline()
    {
        FakeGitHub github = GitHubWithDefaults();

        await Compose(Request(overrides: """{"Ability.201.EffectValue":18,"Ability.200.EffectValue":9}"""), github);

        Assert.Equal("{\n  \"Ability.200.EffectValue\": 9,\n  \"Ability.201.EffectValue\": 18\n}\n", github.Puts.Single().Text);
    }

    [Fact]
    public async Task Put_a_changed_config_file_in_canonical_form_and_a_file_missing_at_the_commit_without_a_sha()
    {
        FakeGitHub github = GitHubWithDefaults();
        BalanceConfigDto edited = WireMapping.ToDto(Host.Defaults) with
        {
            Targets = WireMapping.ToDto(Host.Defaults).Targets.Replace("\"yellowTolerancePct\": ", "\"yellowTolerancePct\": 1", StringComparison.Ordinal),
        };
        Assert.NotEqual(WireMapping.ToDto(Host.Defaults).Targets, edited.Targets);
        github.FilesAtCommit.Remove("balance/overrides.json");

        await Compose(Request(overrides: """{"Ability.201.EffectValue":18}""", config: edited), github);

        Assert.Equal(["balance/overrides.json", "balance/targets.json"], github.Puts.Select(p => p.Path));
        Assert.Null(github.Puts[0].ExistingSha);
        Assert.Equal("sha-targets", github.Puts[1].ExistingSha);
        Assert.EndsWith("\n", github.Puts[1].Text);
        Assert.DoesNotContain("\r", github.Puts[1].Text);
    }

    [Fact]
    public async Task Name_the_branch_from_the_title_and_the_clock()
    {
        FakeGitHub github = GitHubWithDefaults();

        ExportResultDto result = await Compose(Request(title: "  Slam: +10% damage!  ", overrides: """{"Ability.201.EffectValue":18}"""), github);

        Assert.Equal("balance/slam-10-damage-20260930-1405", result.Branch);
    }

    [Theory]
    [InlineData("!!!", "tuning")]
    [InlineData("日本語", "tuning")]
    public async Task Fall_back_to_tuning_when_the_title_has_no_usable_characters(string title, string slug)
    {
        FakeGitHub github = GitHubWithDefaults();

        ExportResultDto result = await Compose(Request(title: title, overrides: """{"Ability.201.EffectValue":18}"""), github);

        Assert.Equal($"balance/{slug}-20260930-1405", result.Branch);
    }

    [Fact]
    public async Task Cut_the_slug_to_40_characters_without_a_trailing_dash()
    {
        FakeGitHub github = GitHubWithDefaults();
        string title = new string('a', 39) + " bbbbbbbb";

        ExportResultDto result = await Compose(Request(title: title, overrides: """{"Ability.201.EffectValue":18}"""), github);

        Assert.Equal($"balance/{new string('a', 39)}-20260930-1405", result.Branch);
    }

    [Fact]
    public async Task Pick_a_new_branch_name_when_one_exists()
    {
        FakeGitHub github = GitHubWithDefaults();
        github.Branches.Add("balance/slam-tuning-20260930-1405");
        github.Branches.Add("balance/slam-tuning-20260930-1405-2");

        ExportResultDto result = await Compose(Request(overrides: """{"Ability.201.EffectValue":18}"""), github);

        Assert.Equal("balance/slam-tuning-20260930-1405-3", result.Branch);
        Assert.Equal(result.Branch, github.CreatedBranch);
    }

    [Fact]
    public async Task Give_up_with_a_502_style_failure_after_50_clashing_branch_names()
    {
        FakeGitHub github = GitHubWithDefaults();
        github.Branches.Add("balance/slam-tuning-20260930-1405");
        for (int i = 2; i <= 60; i++)
            github.Branches.Add($"balance/slam-tuning-20260930-1405-{i}");

        ExportFailedException e = await Assert.ThrowsAsync<ExportFailedException>(() =>
            Compose(Request(overrides: """{"Ability.201.EffectValue":18}"""), github));

        Assert.Equal(50, github.Calls.Count(c => c == "BranchExists"));
        Assert.Null(github.CreatedBranch);
        Assert.Empty(github.Puts);
        Assert.Contains("branch", e.Message);
    }

    [Fact]
    public async Task Refuse_with_nothing_to_export_when_the_input_matches_the_commit()
    {
        FakeGitHub github = GitHubWithDefaults();

        ExportInvalidException e = await Assert.ThrowsAsync<ExportInvalidException>(() => Compose(Request(), github));

        Assert.Contains(e.Issues, i => i.Message.Contains("nothing to export", StringComparison.Ordinal));
        Assert.Empty(github.Puts);
        Assert.Empty(github.PullRequests);
        Assert.DoesNotContain("CreateBranch", github.Calls);
    }

    [Fact]
    public async Task Drop_overrides_equal_to_the_seed_so_they_do_not_count_as_a_change()
    {
        FakeGitHub github = GitHubWithDefaults();

        // Ability 200's EffectValue is 12 in the seed.
        ExportInvalidException e = await Assert.ThrowsAsync<ExportInvalidException>(() =>
            Compose(Request(overrides: """{"Ability.200.EffectValue":12}"""), github));

        Assert.Contains(e.Issues, i => i.Message.Contains("nothing to export", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuse_an_invalid_override_with_issues_and_make_no_github_calls()
    {
        FakeGitHub github = GitHubWithDefaults();

        ExportInvalidException e = await Assert.ThrowsAsync<ExportInvalidException>(() =>
            Compose(Request(overrides: """{"Ability.999.EffectValue":1}"""), github));

        Assert.Contains(e.Issues, i => i.Path.StartsWith("overrides", StringComparison.Ordinal) && i.Message.Contains("999", StringComparison.Ordinal));
        Assert.Empty(github.Calls);
    }

    [Fact]
    public async Task Refuse_a_config_that_does_not_parse_with_issues_and_make_no_github_calls()
    {
        FakeGitHub github = GitHubWithDefaults();
        BalanceConfigDto broken = WireMapping.ToDto(Host.Defaults) with { Scenarios = "{not json" };

        ExportInvalidException e = await Assert.ThrowsAsync<ExportInvalidException>(() => Compose(Request(config: broken), github));

        Assert.Contains(e.Issues, i => i.Path == "config.scenarios");
        Assert.Empty(github.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    public async Task Refuse_a_blank_or_multiline_title_and_make_no_github_calls(string title)
    {
        FakeGitHub github = GitHubWithDefaults();

        ExportInvalidException e = await Assert.ThrowsAsync<ExportInvalidException>(() =>
            Compose(Request(title: title, overrides: """{"Ability.201.EffectValue":18}"""), github));

        Assert.Contains(e.Issues, i => i.Path == "title");
        Assert.Empty(github.Calls);
    }

    [Fact]
    public async Task Not_end_the_pull_request_title_with_a_period()
    {
        FakeGitHub github = GitHubWithDefaults();

        await Compose(Request(title: "Slam tuning.", overrides: """{"Ability.201.EffectValue":18}"""), github);

        Assert.Equal("chore(balance): Slam tuning", github.PullRequests.Single().Title);
    }

    [Fact]
    public async Task Put_the_tweak_table_the_changed_files_the_notes_and_the_player_note_in_the_body()
    {
        FakeGitHub github = GitHubWithDefaults();

        await Compose(Request(overrides: """{"Ability.201.EffectValue":18}""", notes: "Slam hit too hard."), github);

        string body = github.PullRequests.Single().Body;
        Assert.Contains("Slam hit too hard.", body);
        Assert.Contains("| Ability.201.EffectValue | 25 | 18 |", body);
        Assert.Contains("balance/overrides.json", body);
        Assert.DoesNotContain("balance/targets.json", body);
        Assert.EndsWith("Player note: No gameplay changes: balance tuning proposal.", body.TrimEnd());
        Assert.Equal("chore(balance): Slam tuning", github.PullRequests.Single().Title);
    }

    [Fact]
    public async Task Leave_the_closing_line_as_the_only_player_note_line_whatever_the_notes_say()
    {
        FakeGitHub github = GitHubWithDefaults();
        string notes = "Player note: Added X.\n  > player NOTE: y\n- Player   Note : z\r\nsee also PLAYER NOTE: inline";

        await Compose(Request(overrides: """{"Ability.201.EffectValue":18}""", notes: notes), github);

        string body = github.PullRequests.Single().Body;
        string[] matching = body.Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, "(?i)player note:")).ToArray();
        Assert.Equal(["Player note: No gameplay changes: balance tuning proposal."], matching);
        Assert.Contains("Added X.", body);
        Assert.Contains("inline", body);
    }

    [Theory]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0x85)]
    [InlineData(0x0D)]
    public async Task Refuse_a_title_with_any_line_separator(int separator)
    {
        string title = "two" + (char)separator + "lines";
        FakeGitHub github = GitHubWithDefaults();

        ExportInvalidException e = await Assert.ThrowsAsync<ExportInvalidException>(() =>
            Compose(Request(title: title, overrides: """{"Ability.201.EffectValue":18}"""), github));

        Assert.Contains(e.Issues, i => i.Path == "title");
        Assert.Empty(github.Calls);
    }

    [Fact]
    public async Task Add_the_run_summary_and_its_ten_worst_metrics_to_the_body()
    {
        FakeGitHub github = GitHubWithDefaults();
        var metrics = Enumerable.Range(0, 12)
            .Select(i => new MetricDto("Warrior|3|none|normal-3", "check", $"metric-{i}", 100 + i, new BandDto(0, 10), "pct", "Red"))
            .Append(new MetricDto(null, "check", "fine-metric", 5, new BandDto(0, 10), "pct", "Green"))
            .ToList();
        var run = new RunResultDto([], metrics, [], new SummaryDto(1, 2, 12), [], [], 1, 10);

        await Compose(Request(overrides: """{"Ability.201.EffectValue":18}""", runId: "run1"), github, run);

        string body = github.PullRequests.Single().Body;
        Assert.Contains("green 1", body);
        Assert.Contains("yellow 2", body);
        Assert.Contains("red 12", body);
        Assert.Contains("metric-11", body);
        Assert.Contains("metric-2", body);
        Assert.DoesNotContain("metric-1 ", body);
        Assert.DoesNotContain("metric-0 ", body);
        Assert.DoesNotContain("fine-metric", body);
    }

    [Fact]
    public async Task Name_the_failed_step_and_the_status_when_github_refuses()
    {
        FakeGitHub github = GitHubWithDefaults();
        github.Fail = ("CreateBranch", 403);

        ExportFailedException e = await Assert.ThrowsAsync<ExportFailedException>(() =>
            Compose(Request(overrides: """{"Ability.201.EffectValue":18}"""), github));

        Assert.Contains("create branch", e.Message);
        Assert.Contains("403", e.Message);
        Assert.Empty(github.PullRequests);
    }

    [Fact]
    public async Task Name_the_open_pull_request_step_when_the_pull_request_is_refused()
    {
        FakeGitHub github = GitHubWithDefaults();
        github.Fail = ("OpenPullRequest", 422);

        ExportFailedException e = await Assert.ThrowsAsync<ExportFailedException>(() =>
            Compose(Request(overrides: """{"Ability.201.EffectValue":18}"""), github));

        Assert.Contains("open pull request", e.Message);
        Assert.Contains("422", e.Message);
    }
}
