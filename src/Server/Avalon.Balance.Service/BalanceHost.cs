using System.Reflection;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Mapping;

namespace Avalon.Balance.Service;

/// <summary>The seed tables and the checked-in defaults, loaded once at startup and shared by every request.</summary>
public sealed class BalanceHost
{
    private readonly Lazy<CatalogDto> _catalog;

    public BalanceHost(SeedTables seed, BalanceConfig defaults)
    {
        Seed = seed;
        Defaults = defaults;
        _catalog = new Lazy<CatalogDto>(BuildCatalog);
    }

    public SeedTables Seed { get; }

    public BalanceConfig Defaults { get; }

    public CatalogDto Catalog => _catalog.Value;

    /// <summary>The informational version is <c>X.Y.Z+sha</c> in CI; the commit is <c>unknown</c> without one.</summary>
    public static (string Version, string Commit) ReadVersion()
    {
        string? info = typeof(BalanceHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
            return ("unknown", "unknown");

        int plus = info.IndexOf('+');
        if (plus < 0)
            return (info, "unknown");

        string commit = info[(plus + 1)..];
        return (info[..plus], commit.Length > 0 ? commit : "unknown");
    }

    private CatalogDto BuildCatalog()
    {
        (string version, string commit) = ReadVersion();
        ScenarioFile scenarios = Defaults.Scenarios;
        return new CatalogDto(
            version,
            commit,
            Core.Catalog.Describe(Seed).Select(WireMapping.ToDto).ToList(),
            WireMapping.ToDto(Defaults),
            scenarios.Classes.Select(c => c.ToString()).ToList(),
            scenarios.LevelRange().Select(l => (int)l).ToList(),
            scenarios.Gear.ToList(),
            scenarios.Scenarios.Select(s => s.Id).ToList(),
            Seed.AbilityTemplates.Select(a => new NamedIdDto(a.Id.Value, a.Name)).ToList(),
            Seed.CreatureTemplates.Select(c => new NamedIdDto((long)c.Id.Value, c.Name)).ToList());
    }
}
