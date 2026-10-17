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
/// </summary>
internal readonly record struct PendingHit(IUnit Caster, IAbility Ability, ICharacter Target, AbilityFootprint Footprint,
    long DueTick);

/// <summary>
/// The dodge wait's hits (#1082): a telegraphed cast's hits on players, judged by each player's screen. The player's client
/// ends the countdown when the cast fires on the server, one way later; the inputs it makes then reach the server one way
/// after that, so the hit waits that long for them and lands only if the player is still in the footprint. It can only
/// remove a hit: a player outside the footprint when it fired was never queued here. Tick thread only, as every hit is.
/// </summary>
/// <remarks>
/// One list for the instance's life, resolved by index and emptied by swap-remove: once it has grown to the most hits ever
/// waiting at once, adding and resolving allocate nothing (#880).
/// </remarks>
internal sealed class PendingHits(ILogger logger)
{
    private readonly List<PendingHit> _hits = new(16);

    public void Add(in PendingHit hit) => _hits.Add(hit);

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

            // The last hit takes its place and is looked at next.
            RemoveAt(i);

            if (InstanceAbilityCastSystem.IsAbandonedByCreature(hit.Caster))
            {
                CombatLog.HeldHitDroppedCreatureGone(logger, hit.Ability.AbilityId, hit.Caster.Guid);
                continue;
            }

            ICharacter target = hit.Target;
            if (target.IsDead
                || !AbilityEffect.Qualifies(arena, hit.Caster, hit.Ability, target)
                || !hit.Footprint.Overlaps(target.Position, target.BodyRadius))
            {
                continue;
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
                RemoveAt(i);
        }
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
