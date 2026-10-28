using Avalon.World.Abilities.Targeting;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

/// <summary>
/// A telegraph's hit on a player, held for that player's one-way delay (#1082): due on <paramref name="DueTick" />, when it
/// lands only if <paramref name="Target" /> is still inside <paramref name="Footprint" />, the one the cast fired with.
/// Held, its <paramref name="Ability" /> is the list's own copy of the cast's (<see cref="PendingHits.Add" />), never the
/// script's.
/// </summary>
public readonly record struct PendingHit(IUnit Caster, IAbility Ability, ICharacter Target, AbilityFootprint Footprint,
    long DueTick);

/// <summary>
/// The dodge wait's hits (#1082): a telegraphed cast's hits on players, judged by each player's screen. The player's client
/// ends the countdown when the cast fires on the server, one way later; the inputs it makes then reach the server one way
/// after that, so the hit waits that long for them and lands only if the player is still in the footprint. It can only
/// remove a hit: a player outside the footprint when it fired was never queued here. Tick thread only, as every hit is.
/// </summary>
/// <remarks>
/// <para>
/// One list for the instance's life, resolved by index and emptied by swap-remove: once it has grown to the most hits ever
/// waiting at once, adding and resolving allocate nothing (#880).
/// </para>
/// <para>
/// <b>Its own copy of each hit's ability (#1238).</b> A circle's or a cone's script is pooled, and its copy of the ability
/// is cleared when it goes back to its pool, on the tick it fired, and made again for its next cast, so a hit must not hold
/// it. Each hit keeps a copy of its own, taken from this list's free copies when it is added
/// (<see cref="GameAbility.CopyFrom" />: the id, the metadata, so the shape, and the timers the cast fired with) and
/// cleared and freed once the hit has landed, been dodged or been dropped. So a held hit lands with its own cast's ability
/// whatever its script does next, and a free copy keeps nothing alive. The free copies are never more than the most hits
/// ever waiting at once, so warm, a held hit allocates nothing. An ability that is not a <see cref="GameAbility" /> (one a
/// script built outside the World made) is held as it is: its script never clears it.
/// </para>
/// <para>
/// Public rather than internal, with <see cref="PendingHit" />, because Avalon.World is strong-named and the test assembly
/// is not, so an InternalsVisibleTo cannot name it; Avalon.World is not the modding API.
/// </para>
/// </remarks>
public sealed class PendingHits(ILogger logger)
{
    private readonly List<PendingHit> _hits = new(16);
    private readonly Stack<GameAbility> _freeCopies = new();

    /// <summary>Holds <paramref name="hit" /> with a copy of its ability of the list's own.</summary>
    public void Add(in PendingHit hit) => _hits.Add(hit with { Ability = Hold(hit.Ability) });

    /// <summary>
    /// Lands every hit due by <paramref name="nowTick" /> on a target still alive, still a target of its ability and still
    /// inside its footprint where it stands now, through <see cref="AbilityEffect.Apply" />, as an immediate hit would; any
    /// other due hit is a dodge and does nothing. A creature caster that died or turned for home in the wait lands nothing,
    /// as its queued casts and projectiles do not (#163): its death or its turn already took it out of the encounter, and
    /// the hit would put its corpse back in, or pull its walk home back into the fight (#614). A player caster who died in
    /// the wait still lands its hits: the cast fired. Each hit leaves the list before it is applied, and each is contained,
    /// so a hit that throws, or a hit that leads to another being added or dropped, cannot apply one twice or cost the
    /// others theirs.
    /// </summary>
    /// <remarks>
    /// Nothing here checks that the caster or the target is still in the instance: every path out of it calls
    /// <c>CancelCasts</c>, which drops the leaving unit's hits (<see cref="Drop" />) before it is gone. A new leave path
    /// that skipped it would land a held hit on, or from, a unit no longer here.
    /// </remarks>
    public void Resolve(long nowTick, IAbilityArena arena)
    {
        int i = 0;
        while (i < _hits.Count)
        {
            PendingHit hit = _hits[i];
            if (hit.DueTick > nowTick)
            {
                i++;
                continue;
            }

            // The last hit takes its place and is looked at next. Its copy is freed only once it is settled, so a hit added
            // while it applies never takes it.
            RemoveAt(i);
            try
            {
                Land(hit, arena);
            }
            finally
            {
                Free(hit.Ability);
            }
        }
    }

    /// <summary>One due hit, out of the list: landed, or a dodge or a drop that does nothing.</summary>
    private void Land(in PendingHit hit, IAbilityArena arena)
    {
        if (InstanceAbilityCastSystem.IsAbandonedByCreature(hit.Caster))
        {
            CombatLog.HeldHitDroppedCreatureGone(logger, hit.Ability.AbilityId, hit.Caster.Guid);
            return;
        }

        ICharacter target = hit.Target;
        if (target.IsDead
            || !AbilityEffect.Qualifies(arena, hit.Caster, hit.Ability, target)
            || !hit.Footprint.Overlaps(target.Position, target.BodyRadius))
        {
            return;
        }

        try
        {
            AbilityEffect.Apply(arena, hit.Caster, hit.Ability, target);
        }
        catch (Exception e)
        {
            CombatLog.HeldHitFailed(logger, e, hit.Ability.AbilityId, hit.Caster.Guid, target.Guid);
        }
    }

    /// <summary>
    /// Drops every hit with <paramref name="unit" /> as its caster or its target, for a unit leaving the instance: a caster
    /// who is gone credits nothing here, and a target who is gone is not here to be hit.
    /// </summary>
    public void Drop(IUnit unit)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            PendingHit hit = _hits[i];
            if (ReferenceEquals(hit.Caster, unit) || ReferenceEquals(hit.Target, unit))
            {
                RemoveAt(i);
                Free(hit.Ability);
            }
        }
    }

    /// <summary>A copy of <paramref name="ability" /> of the list's own, a free one made again when there is one.</summary>
    private IAbility Hold(IAbility ability)
    {
        if (ability is not GameAbility cast)
            return ability;

        if (!_freeCopies.TryPop(out GameAbility? copy))
            return cast.CloneForCast();

        copy.CopyFrom(cast);
        return copy;
    }

    /// <summary>A held hit's copy, cleared (no id, no metadata) and free again. Any other ability is left as it is.</summary>
    private void Free(IAbility held)
    {
        if (held is not GameAbility copy)
            return;

        copy.Clear();
        _freeCopies.Push(copy);
    }

    /// <summary>Swap-remove: the last hit moves into <paramref name="index" />, and the list's last slot is cleared.</summary>
    private void RemoveAt(int index)
    {
        int last = _hits.Count - 1;
        if (index != last)
            _hits[index] = _hits[last];
        _hits.RemoveAt(last);
    }
}
