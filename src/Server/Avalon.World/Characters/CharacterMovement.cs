using Avalon.Domain.World;

namespace Avalon.World.Characters;

/// <summary>
/// A character's movement speed (#627): the base 4 m/s times <c>1 + bonus / 100</c>, where the bonus is the
/// worn gear's MovementSpeed percentage clamped to the combat formula's MoveSpeedFloor and MoveSpeedCap.
/// Worked out at every stats refresh; the input step reads the result.
/// </summary>
public static class CharacterMovement
{
    /// <summary>Metres per second with no bonus.</summary>
    public const float BaseSpeed = 4.0f;

    /// <summary>
    /// The client predicts its own steps at the speed it last heard and snaps once it drifts more than
    /// this from the server, in metres. A speed change reaches it with the character sheet, a round trip
    /// after the server starts using it.
    /// </summary>
    public const float ClientSnapThreshold = 0.15f;

    /// <summary>
    /// Metres per second. The floor and cap are validated finite with -100 &lt; floor &lt;= cap, so the result
    /// is always above 0.
    /// </summary>
    public static float SpeedFor(float gearPct, CombatFormula formula)
    {
        // Min of max, not Math.Clamp, which throws on a floor above the cap: this runs on the tick.
        float bonus = MathF.Min(MathF.Max(gearPct, formula.MoveSpeedFloor), formula.MoveSpeedCap);
        return BaseSpeed * (1f + bonus / 100f);
    }
}
