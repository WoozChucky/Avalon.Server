using System.Text.Json;

namespace Avalon.Balance.Core;

/// <summary>One run: overrides over the seed, optionally other config than the defaults, a filter, runs and seed.</summary>
public sealed record RunRequest(JsonElement? Overrides, BalanceConfig? Config, RunFilter Filter, int? RunsPerRow, int? Seed);
