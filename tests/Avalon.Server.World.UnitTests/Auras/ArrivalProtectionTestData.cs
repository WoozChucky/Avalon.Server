using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Combat;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>Loads the arrival fixture through StaticData and applies it through a real instance aura system.</summary>
internal static class ArrivalProtectionTestData
{
    public static AuraSystem System(TimeProvider time)
    {
        StaticData data = TestStaticData.LoadAsync(TestStaticData.Repositories(
            auras: static () => [AuraTestData.ArrivalProtection()])).GetAwaiter().GetResult();
        var config = new CombatConfig();
        var combat = new CombatService(config, new EncounterRegistry(config, time), time: time);
        return new AuraSystem(combat, [], [], () => data.Auras, () => data, time, 32,
            TimeSpan.FromSeconds(6), NullLogger.Instance);
    }

    public static void Apply(CharacterEntity character)
    {
        AuraSystem system = System(character.Clock);
        system.Roster.Join(character);
        Assert.Equal(AuraApplyResult.Applied, ArrivalProtection.Start(character, system));
    }
}
