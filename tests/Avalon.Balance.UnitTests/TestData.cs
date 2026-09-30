using Avalon.Balance.Data;

namespace Avalon.Balance.UnitTests;

/// <summary>The seeded data, loaded once. Read-only: a test that changes tables reads its own SeedTables.</summary>
internal static class TestData
{
    private static readonly Lazy<BalanceData> SeededData = new(() => BalanceData.From(SeedTables.Read()));

    public static BalanceData Seeded => SeededData.Value;
}
