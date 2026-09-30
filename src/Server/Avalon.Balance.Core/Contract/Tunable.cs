namespace Avalon.Balance.Core;

/// <summary>One value an override can change: its key, where it lives, its type, and what the seed holds.</summary>
public sealed record Tunable(string Key, string Table, string? RowKey, string Column, string Type, string SeedValue, string Display);
