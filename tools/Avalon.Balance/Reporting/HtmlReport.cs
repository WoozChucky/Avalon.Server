using System.Globalization;
using System.Net;
using System.Text;
using Avalon.Balance.Core;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Reporting;

public sealed record ReportContext(DateTimeOffset GeneratedAt, string Commit, int Seed, int Runs, OverrideReport Overrides,
    IReadOnlyList<RowResult> Rows, GradeReport Grades, ScenarioFile Scenarios, TargetFile Targets);

/// <summary>One self-contained page: inline CSS and SVG, no scripts, light and dark.</summary>
public static class HtmlReport
{
    private static readonly CharacterClass[] s_classOrder =
        [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Healer];

    private const string Css = """
        :root{color-scheme:light;--page:#f9f9f7;--surface:#fcfcfb;--ink:#0b0b0b;--ink2:#52514e;--muted:#898781;--grid:#e1e0d9;--axis:#c3c2b7;--ring:rgba(11,11,11,.10);
          --s1:#2a78d6;--s2:#eb6834;--s3:#1baf7a;--s4:#eda100;--good:#0ca30c;--warn:#fab219;--bad:#d03b3b;--band:rgba(12,163,12,.12)}
        @media (prefers-color-scheme:dark){:root:not([data-theme="light"]){color-scheme:dark;--page:#0d0d0d;--surface:#1a1a19;--ink:#fff;--ink2:#c3c2b7;--grid:#2c2c2a;--axis:#383835;--ring:rgba(255,255,255,.10);
          --s1:#3987e5;--s2:#d95926;--s3:#199e70;--s4:#c98500;--band:rgba(12,163,12,.18)}}
        :root[data-theme="dark"]{color-scheme:dark;--page:#0d0d0d;--surface:#1a1a19;--ink:#fff;--ink2:#c3c2b7;--grid:#2c2c2a;--axis:#383835;--ring:rgba(255,255,255,.10);
          --s1:#3987e5;--s2:#d95926;--s3:#199e70;--s4:#c98500;--band:rgba(12,163,12,.18)}
        body{margin:0;background:var(--page);color:var(--ink);font:14px/1.45 system-ui,sans-serif}
        main{max-width:1200px;margin:0 auto;padding:16px}
        section{background:var(--surface);border:1px solid var(--ring);border-radius:8px;padding:16px;margin:16px 0}
        h1{font-size:22px;margin:0 0 4px}h2{font-size:18px;margin:0 0 12px}h3{font-size:15px;margin:16px 0 8px}
        .meta{color:var(--ink2)}.muted{color:var(--muted)}
        .scroll{overflow-x:auto}
        table{border-collapse:collapse;font-variant-numeric:tabular-nums}
        th,td{border-bottom:1px solid var(--grid);padding:4px 8px;text-align:left;vertical-align:top;white-space:nowrap}
        .g-green{background:color-mix(in srgb,var(--good) 16%,transparent)}
        .g-yellow{background:color-mix(in srgb,var(--warn) 22%,transparent)}
        .g-red{background:color-mix(in srgb,var(--bad) 20%,transparent)}
        .badge{display:inline-block;min-width:1.4em;text-align:center;border-radius:4px;font-weight:600;font-size:11px;margin-left:4px;border:1px solid var(--ring)}
        .tiles{display:flex;gap:12px;flex-wrap:wrap}.tile{border:1px solid var(--ring);border-radius:8px;padding:8px 16px}.tile b{font-size:22px;display:block}
        figure{margin:8px 0 16px}figcaption{color:var(--ink2);margin-bottom:4px}
        svg{width:100%;max-width:640px;height:auto}svg text{fill:var(--muted);font-size:11px}
        .gridline{stroke:var(--grid)}.axis{stroke:var(--axis)}.bandrect{fill:var(--band)}
        .line{fill:none;stroke-width:2}.c1{stroke:var(--s1);fill:var(--s1)}.c2{stroke:var(--s2);fill:var(--s2)}.c3{stroke:var(--s3);fill:var(--s3)}.c4{stroke:var(--s4);fill:var(--s4)}
        .line.c1,.line.c2,.line.c3,.line.c4{fill:none}
        .legend{display:flex;gap:12px;color:var(--ink2)}.sw{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:4px}
        .sw.c1{background:var(--s1)}.sw.c2{background:var(--s2)}.sw.c3{background:var(--s3)}.sw.c4{background:var(--s4)}
        details{margin:4px 0}summary{cursor:pointer}
        """;

    public static string Render(ReportContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Balance report</title><style>")
          .Append(Css).Append("</style></head><body><main>");
        Header(sb, ctx);
        Summary(sb, ctx);
        foreach (Scenario scenario in ctx.Scenarios.Scenarios.Where(s => ctx.Rows.Any(r => r.Key.Scenario == s.Id)))
            ScenarioSection(sb, ctx, scenario);
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void Header(StringBuilder sb, ReportContext ctx)
    {
        sb.Append("<section><h1>Balance report</h1><p class=\"meta\">")
          .Append(E(ctx.GeneratedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)))
          .Append(" &middot; commit ").Append(E(ctx.Commit))
          .Append(" &middot; Seed ").Append(ctx.Seed)
          .Append(" &middot; ").Append(ctx.Runs).Append(" runs per row")
          .Append(" &middot; graded gear: ").Append(E(ctx.Targets.GradedGear)).Append("</p>");

        sb.Append("<h3>Active overrides</h3>");
        if (ctx.Overrides.Applied.Count == 0)
        {
            sb.Append("<p class=\"muted\">None.</p>");
        }
        else
        {
            sb.Append("<table><tr><th>Key</th><th>Seed</th><th>Override</th></tr>");
            foreach (AppliedOverride o in ctx.Overrides.Applied)
                sb.Append("<tr><td>").Append(E(o.Key)).Append("</td><td>").Append(E(o.Seed)).Append("</td><td>").Append(E(o.Value)).Append("</td></tr>");
            sb.Append("</table>");
        }

        if (ctx.Overrides.Stale.Count > 0)
            sb.Append("<p>Stale overrides (already applied in the seed): ").Append(E(string.Join(", ", ctx.Overrides.Stale))).Append("</p>");
        sb.Append("</section>");
    }

    private static void Summary(StringBuilder sb, ReportContext ctx)
    {
        sb.Append("<section><h2>Summary</h2><div class=\"tiles\">");
        foreach (Grade g in Enum.GetValues<Grade>())
            sb.Append("<div class=\"tile ").Append(Cls(g)).Append("\"><b>").Append(ctx.Grades.Count(g)).Append("</b>").Append(g).Append(Badge(g)).Append("</div>");
        sb.Append("</div><h3>Worst 10</h3><div class=\"scroll\"><table><tr><th>Grade</th><th>Check</th><th>Row</th><th>Metric</th><th>Value</th><th>Target</th></tr>");
        foreach (GradedMetric m in ctx.Grades.Worst(10))
        {
            sb.Append("<tr><td class=\"").Append(Cls(m.Grade)).Append("\">").Append(Badge(m.Grade)).Append("</td><td>").Append(E(m.Check))
              .Append("</td><td>").Append(m.Row is { } k ? E($"{k.Class} L{k.Level} {k.Scenario}") : "&ndash;")
              .Append("</td><td>").Append(E(m.Metric)).Append("</td><td>").Append(VU(m.Value, m.Unit))
              .Append("</td><td>").Append(E(m.Band.Describe(m.Unit))).Append("</td></tr>");
        }

        sb.Append("</table></div>");
        GlobalChecks(sb, ctx);
        sb.Append("</section>");
    }

    private static void GlobalChecks(StringBuilder sb, ReportContext ctx)
    {
        sb.Append("<details><summary>All global checks</summary><div class=\"scroll\"><table><tr><th>Check</th><th>Metric</th><th>Value</th><th>Target</th></tr>");
        foreach (GradedMetric m in ctx.Grades.Metrics.Where(m => m.Check != "scenario"))
        {
            sb.Append("<tr><td>").Append(E(m.Check)).Append("</td><td>").Append(E(m.Metric)).Append("</td><td class=\"").Append(Cls(m.Grade))
              .Append("\">").Append(VU(m.Value, m.Unit)).Append(Badge(m.Grade)).Append("</td><td>").Append(E(m.Band.Describe(m.Unit)))
              .Append("</td></tr>");
        }

        sb.Append("</table></div></details>");
    }

    private static void ScenarioSection(StringBuilder sb, ReportContext ctx, Scenario scenario)
    {
        sb.Append("<section><h2>").Append(E(scenario.Id)).Append("</h2>");
        var levels = ctx.Rows.Where(r => r.Key.Scenario == scenario.Id).Select(r => r.Key.Level).Distinct().Order().ToList();
        foreach (string gear in ctx.Rows.Where(r => r.Key.Scenario == scenario.Id).Select(r => r.Key.Gear).Distinct())
        {
            bool gradedGear = gear == ctx.Targets.GradedGear;
            sb.Append(gradedGear ? "<h3>" : "<details><summary>").Append("Gear: ").Append(E(gear)).Append(gradedGear ? " (graded)</h3>" : " (ungraded)</summary>");
            Grid(sb, ctx, scenario.Id, gear, levels);
            if (!gradedGear) sb.Append("</details>");
        }

        ctx.Targets.Scenarios.TryGetValue(scenario.Id, out ScenarioTargets? targets);
        sb.Append("<h3>Level curves (").Append(E(ctx.Targets.GradedGear)).Append(")</h3>");
        var curveRows = ctx.Rows.Where(r => r.Key.Scenario == scenario.Id && r.Key.Gear == ctx.Targets.GradedGear).ToList();
        LineChart(sb, "Win rate (%)", levels, Series(curveRows, levels, r => r.WinRatePct), targets?.WinRate, 100, " %");
        double yFight = Math.Max(curveRows.Select(r => r.FightSeconds.Median).DefaultIfEmpty(1).Max(), targets?.FightSeconds?.Max ?? 0) * 1.1;
        LineChart(sb, "Median fight length (s)", levels, Series(curveRows, levels, r => r.FightSeconds.Median), targets?.FightSeconds, yFight, " s");

        sb.Append("<h3>Rows</h3>");
        foreach (RowResult r in ctx.Rows.Where(r => r.Key.Scenario == scenario.Id))
            RowDetails(sb, r);
        sb.Append("</section>");
    }

    private static void Grid(StringBuilder sb, ReportContext ctx, string scenario, string gear, List<ushort> levels)
    {
        sb.Append("<div class=\"scroll\"><table><tr><th>Class</th>");
        foreach (ushort l in levels) sb.Append("<th>L").Append(l).Append("</th>");
        sb.Append("</tr>");
        foreach (CharacterClass c in s_classOrder)
        {
            if (!ctx.Rows.Any(r => r.Key.Class == c && r.Key.Scenario == scenario && r.Key.Gear == gear)) continue;
            sb.Append("<tr><th>").Append(c).Append("</th>");
            foreach (ushort l in levels)
            {
                RowResult? r = ctx.Rows.FirstOrDefault(x => x.Key == new RowKey(c, l, gear, scenario));
                if (r is null) { sb.Append("<td>&ndash;</td>"); continue; }
                GradedMetric[] graded = ctx.Grades.Metrics.Where(m => m.Row == r.Key && m.Check == "scenario").ToArray();
                Grade? worst = graded.Length > 0 ? graded.Max(m => m.Grade) : (Grade?)null;
                string title = string.Join("; ", graded.Select(m => $"{m.Metric} {Plain(m.Value, m.Unit)}, target {m.Band.Describe(m.Unit)}: {m.Grade}"));
                sb.Append("<td").Append(worst is { } w ? $" class=\"{Cls(w)}\"" : "")
                  .Append(title.Length > 0 ? $" title=\"{E(title)}\"" : "").Append('>')
                  .Append(V(r.WinRatePct)).Append(" % win").Append(worst is { } w2 ? Badge(w2) : "")
                  .Append("<br>").Append(V(r.FightSeconds.Median)).Append(" s")
                  .Append("<br>").Append(VU(r.HealthLeftPct?.Median, " %")).Append(" hp")
                  .Append("<br>1st spender ").Append(VU(r.FirstSpenderSeconds?.Median, " s"))
                  .Append("<br>starved ").Append(V(r.StarvedPct.Median)).Append(" %</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table></div>");
    }

    private static IEnumerable<(int Slot, CharacterClass Class, double?[] Values)> Series(List<RowResult> rows, List<ushort> levels,
        Func<RowResult, double> pick) =>
        s_classOrder.Select((c, i) => (Slot: i + 1, Class: c,
                Values: levels.Select(l => rows.FirstOrDefault(r => r.Key.Class == c && r.Key.Level == l) is { } r ? pick(r) : (double?)null).ToArray()))
            .Where(s => s.Values.Any(v => v is not null));

    private static void LineChart(StringBuilder sb, string title, List<ushort> levels,
        IEnumerable<(int Slot, CharacterClass Class, double?[] Values)> series, Band? band, double yMax, string unit)
    {
        // inset keeps the first and last markers (r 4) off the y-axis labels and the plot edge.
        const int W = 560, H = 220, Left = 44, Right = 12, Top = 12, Bottom = 28, Inset = 14;
        yMax = Math.Max(yMax, 1);
        const double Span = W - Left - Right - 2 * Inset;
        double X(int i) => Left + Inset + (levels.Count <= 1 ? Span / 2d : i * Span / (levels.Count - 1));
        double Y(double v) => Top + (H - Top - Bottom) * (1 - Math.Clamp(v / yMax, 0, 1));

        sb.Append("<figure><figcaption>").Append(E(title)).Append(band is null ? "" : $" &middot; target {E(band.Describe(unit))}")
          .Append("</figcaption><svg viewBox=\"0 0 ").Append(W).Append(' ').Append(H).Append("\" role=\"img\" aria-label=\"").Append(E(title)).Append("\">");
        if (band is not null)
        {
            double y1 = Y(band.Max ?? yMax), y2 = Y(band.Min ?? 0);
            sb.Append($"<rect class=\"bandrect\" x=\"{Left}\" y=\"{F(y1)}\" width=\"{W - Left - Right}\" height=\"{F(Math.Max(0, y2 - y1))}\"/>");
        }

        foreach (double tick in new[] { 0, yMax / 2, yMax })
        {
            sb.Append($"<line class=\"gridline\" x1=\"{Left}\" x2=\"{W - Right}\" y1=\"{F(Y(tick))}\" y2=\"{F(Y(tick))}\"/>")
              .Append($"<text x=\"{Left - 6}\" y=\"{F(Y(tick) + 4)}\" text-anchor=\"end\">{V(tick)}</text>");
        }

        sb.Append($"<line class=\"axis\" x1=\"{Left}\" x2=\"{W - Right}\" y1=\"{H - Bottom}\" y2=\"{H - Bottom}\"/>");
        for (int i = 0; i < levels.Count; i++)
            sb.Append($"<text x=\"{F(X(i))}\" y=\"{H - 10}\" text-anchor=\"middle\">L{levels[i]}</text>");

        var legend = new StringBuilder("<div class=\"legend\">");
        foreach ((int slot, CharacterClass c, double?[] values) in series)
        {
            string points = string.Join(' ', values.Select((v, i) => v is { } d ? $"{F(X(i))},{F(Y(d))}" : null).OfType<string>());
            sb.Append($"<polyline class=\"line c{slot}\" points=\"{points}\"/>");
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is not { } d) continue;
                sb.Append($"<circle class=\"c{slot}\" cx=\"{F(X(i))}\" cy=\"{F(Y(d))}\" r=\"4\"><title>{c} L{levels[i]}: {V(d)}{E(unit)}</title></circle>");
            }

            legend.Append($"<span><span class=\"sw c{slot}\"></span>{c}</span>");
        }

        sb.Append("</svg>").Append(legend).Append("</div></figure>");
    }

    private static void RowDetails(StringBuilder sb, RowResult r)
    {
        PlayerSnapshot s = r.Snapshot;
        sb.Append("<details><summary>").Append(E($"{r.Key.Class} L{r.Key.Level} {r.Key.Gear}")).Append(" &middot; ")
          .Append(V(r.WinRatePct)).Append(" % win</summary><div class=\"scroll\">");
        sb.Append("<p>Health ").Append(s.Health).Append(" &middot; power ").Append(s.Power).Append(" &middot; attack ").Append(s.AttackDamage)
          .Append(" &middot; ability ").Append(s.AbilityDamage).Append(" &middot; armour ").Append(s.Armor)
          .Append(" &middot; crit ").Append(V(s.CritPct)).Append(" % &middot; dodge ").Append(V(s.DodgePct))
          .Append(" % &middot; block ").Append(V(s.BlockPct)).Append(" % &middot; haste ").Append(V(s.HastePct)).Append(" %</p>");
        sb.Append("<table><tr><th>Ability</th><th>Per hit</th></tr>");
        foreach (AbilityLine a in s.Abilities)
            sb.Append("<tr><td>").Append(E(a.Name)).Append("</td><td>").Append(a.Min).Append('-').Append(a.Max).Append(' ').Append(E(a.Kind)).Append("</td></tr>");
        sb.Append("</table>");
        Totals(sb, "Damage dealt per run", r.DamageDealtPerRun);
        Totals(sb, "Damage taken per run", r.DamageTakenPerRun);
        sb.Append("</div></details>");
    }

    private static void Totals(StringBuilder sb, string title, IReadOnlyDictionary<string, double> totals)
    {
        sb.Append("<table><tr><th>").Append(E(title)).Append("</th><th></th></tr>");
        foreach ((string key, double value) in totals.OrderByDescending(kv => kv.Value))
            sb.Append("<tr><td>").Append(E(key)).Append("</td><td>").Append(V(value)).Append("</td></tr>");
        sb.Append("</table>");
    }

    private static string Cls(Grade g) => g switch { Grade.Green => "g-green", Grade.Yellow => "g-yellow", _ => "g-red" };

    private static string Badge(Grade g) => $"<span class=\"badge\" title=\"{g}\">{g.ToString()[0]}</span>";

    /// <summary>A value with its unit as unescaped text (for an attribute), or a lone dash when missing.</summary>
    private static string Plain(double? v, string unit) => v is { } d ? d.ToString("0.#", CultureInfo.InvariantCulture) + unit : "\u2013";

    /// <summary>A value with its unit, or a lone dash when missing.</summary>
    private static string VU(double? v, string unit) => v is null ? V(v) : V(v) + E(unit);

    private static string V(double? v) => v is { } d ? d.ToString("0.#", CultureInfo.InvariantCulture) : "&ndash;";

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
