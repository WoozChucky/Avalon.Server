using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>The rule a keystone bends (#1181): the stored numbers, cast from the World's by ordinal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeystoneRule
{
    /// <summary>Blood Price: an ability's cost is paid from current life instead of mana.</summary>
    LifeCost = 1,
    /// <summary>Steady Hand: no hit, heal or aura tick of the holder's is ever critical.</summary>
    NoCrits = 2,
    /// <summary>Unbroken: the ward also absorbs blight and bleeding, and waits twice as long to recharge.</summary>
    WardTakesAll = 3,
}
