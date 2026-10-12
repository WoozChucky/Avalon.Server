using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Abilities;

public class GameAbility : IAbility
{
    public required AbilityId AbilityId { get; init; }

    public required AbilityMetadata Metadata { get; init; }
    public required float CooldownTimer { get; set; }
    public required float CastTimeTimer { get; set; }
    public bool Casting { get; set; }

    public IAbility Clone()
    {
        return new GameAbility
        {
            AbilityId = AbilityId,
            Metadata = Metadata.Clone(),
            CooldownTimer = CooldownTimer,
            CastTimeTimer = CastTimeTimer
        };
    }

    /// <summary>
    /// The copy a cast's script is handed (#925): timers and a casting flag of its own, so nothing a script does to
    /// its ability reaches its caster's cooldown, cast timer or casting flag, over this ability's own metadata.
    /// The metadata is shared, where <see cref="Clone" /> copies it, because it cannot change once built: every member
    /// is an init-only number, flag, enum or string (<c>AbilityMetadataShould</c>), and a reload or a template edit
    /// makes new metadata for the abilities loaded after it and never rewrites one in use, so a cast in flight keeps
    /// the row it started with.
    /// </summary>
    public GameAbility CloneForCast() =>
        new()
        {
            AbilityId = AbilityId,
            Metadata = Metadata,
            CooldownTimer = CooldownTimer,
            CastTimeTimer = CastTimeTimer,
        };
}
