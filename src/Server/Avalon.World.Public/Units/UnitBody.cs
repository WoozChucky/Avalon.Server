namespace Avalon.World.Public.Units;

/// <summary>
/// Body sizes for skill hit tests (#164). A skill affects a unit when its shape overlaps the unit's
/// body circle on X/Z.
/// </summary>
public static class UnitBody
{
    /// <summary>Metres. Every character's body radius; characters have no per-row size.</summary>
    public const float CharacterRadius = 0.5f;

    /// <summary>Metres. A creature template's body radius when it does not set one.</summary>
    public const float DefaultCreatureRadius = 0.5f;
}
