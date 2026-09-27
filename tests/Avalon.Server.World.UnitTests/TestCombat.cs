using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;

namespace Avalon.Server.World.UnitTests;

/// <summary>The seeded combat area (#506), validated as StaticData would load it.</summary>
internal static class TestCombat
{
    public static CombatPatch Seeded() => CombatPatch.Build([CombatSeed.Formula()], CombatSeed.ClassFactors());

    public static IReadOnlyDictionary<CharacterClass, ClassStatFactors> Factors => Seeded().Factors;

    public static ClassStatFactors FactorsOf(CharacterClass @class) => Factors[@class];
}
