using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Abilities;

public class GameAbility : IAbility
{
    private AbilityId _abilityId = null!;
    private AbilityMetadata _metadata = null!;

    public required AbilityId AbilityId
    {
        get => _abilityId;
        init => _abilityId = value;
    }

    public required AbilityMetadata Metadata
    {
        get => _metadata;
        init => _metadata = value;
    }

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
    /// the row it started with. The character's ability may swap its metadata for another when a capstone reshapes it
    /// (<see cref="SetMetadata" />, #1181 Part D), but the copy keeps the reference it was made with, so a cast never
    /// sees the swap and fires the shape it was queued with.
    /// </summary>
    public GameAbility CloneForCast() =>
        new()
        {
            AbilityId = AbilityId,
            Metadata = Metadata,
            CooldownTimer = CooldownTimer,
            CastTimeTimer = CastTimeTimer,
        };

    /// <summary>
    /// This copy made again as <paramref name="source" />'s cast copy, as <see cref="CloneForCast" /> makes one: its id and
    /// metadata, both timers, and <c>Casting</c> clear. For a pooled script's own copy (#1181 Part D), so a reused script
    /// fires the shape its cast was taken with.
    /// </summary>
    internal void CopyFrom(GameAbility source)
    {
        _abilityId = source._abilityId;
        _metadata = source._metadata;
        CooldownTimer = source.CooldownTimer;
        CastTimeTimer = source.CastTimeTimer;
        Casting = false;
    }

    /// <summary>
    /// A pooled script's copy put away (#1181 Part D): no id and no metadata, so it keeps no template alive, one from before
    /// a reload included. Nothing reads it until <see cref="CopyFrom" />.
    /// </summary>
    internal void Clear()
    {
        _abilityId = null!;
        _metadata = null!;
        CooldownTimer = 0f;
        CastTimeTimer = 0f;
        Casting = false;
    }

    /// <summary>
    /// Swaps the metadata in place: only <see cref="TreeAbility" />, when a capstone taken or refunded reshapes it.
    /// </summary>
    private protected void SetMetadata(AbilityMetadata metadata) => _metadata = metadata;
}
