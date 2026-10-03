using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Auras;

/// <summary>
/// At select: the saved auras come back as they were, their time having stood still while the character was out of the
/// world, and it stands still on until the character enters its instance (<see cref="UnitAuras.ResumeHeld" />), so the
/// loading screen costs none of it. Each keeps its snapshot, stacks, ticks owed, carried fraction of a point and, when a
/// character applied it, its caster; it is held under the template loaded now. A row whose aura is no longer loaded, or
/// whose snapshot cannot be believed, is dropped with a warning. Restoring owes the client nothing: it gets a list when
/// the character enters the world. A restored stat aura refills the pools as select does. Tick thread.
/// </summary>
public static class AuraRestore
{
    /// <summary>
    /// <see cref="Restore" />, contained: a throw costs the auras, never the select. The character is left holding none,
    /// and its stats are refreshed again (contained too) so nothing a restored aura folded into them remains.
    /// </summary>
    public static void RestoreOrNone(CharacterEntity character, IReadOnlyCollection<CharacterAura> rows, StaticData data,
        int maxAuras, ILogger logger)
    {
        try
        {
            Restore(character, rows, data, maxAuras, logger);
        }
        catch (Exception e)
        {
            character.Auras.Load([]);
            logger.LogError(e, "Restoring the auras of character {CharacterId} at select failed; it enters with none",
                character.Guid.Id);

            try
            {
                CharacterStatsRefresh.Apply(character, data, CurrentValues.EnterWorld);
            }
            catch (Exception refresh)
            {
                logger.LogError(refresh, "Refreshing the stats of character {CharacterId} after its auras were dropped failed",
                    character.Guid.Id);
            }
        }
    }

    /// <param name="maxAuras">At most this many come back, the earliest applied first (Game:MaxAurasPerUnit).</param>
    public static void Restore(CharacterEntity character, IReadOnlyCollection<CharacterAura> rows, StaticData data,
        int maxAuras, ILogger logger)
    {
        DateTimeOffset now = character.Clock.GetUtcNow();
        var kept = new List<(CharacterAura Row, ActiveAura Aura)>(rows.Count);
        foreach (CharacterAura row in rows)
        {
            if (!data.Auras.TryGet(new AuraId(row.AuraId), out AuraTemplate? template))
            {
                logger.LogWarning("Dropped the saved aura {AuraId} of character {CharacterId}: it is not loaded",
                    row.AuraId, character.Guid.Id);
                continue;
            }

            if (!Readable(row.TickAmount) || !Readable(row.CritPct))
            {
                logger.LogWarning(
                    "Dropped the saved aura {AuraId} of character {CharacterId}: its tick amount {TickAmount} or crit chance {CritPct} is not a number of 0 or more",
                    row.AuraId, character.Guid.Id, row.TickAmount, row.CritPct);
                continue;
            }

            AuraSchedule schedule = AuraSchedule.Restore(now, row.RemainingMs, template.TickIntervalMs, row.TicksLeft);
            if (row.RemainingMs == 0 && schedule.TicksLeft == 0)
                continue;   // over, with nothing left to deal

            AuraSource source = row.SourceAbilityId is { } abilityId &&
                                data.Abilities.TryGet(new AbilityId(abilityId), out AbilityTemplate? ability)
                ? AuraSource.Of(ability)
                : AuraSource.None;

            // No more than the template allows now: a reload may have lowered its cap.
            uint stacks = row.Stacks < 1 ? 1u : Math.Min((uint)row.Stacks, Math.Max(1u, template.MaxStacks));
            kept.Add((row, new ActiveAura(template, Caster(row.CasterGuid), source, stacks,
                new AuraSnapshot(row.TickAmount, row.CritPct, (ushort)Math.Clamp(row.CasterLevel, 0, ushort.MaxValue)),
                schedule, row.DurationMs, row.AppliedAt)
            {
                PeriodicCarry = Carry(row.PeriodicCarry),
            }));
        }

        int limit = Math.Max(0, maxAuras);
        if (kept.Count > limit)
        {
            logger.LogWarning("Dropped {Count} saved auras of character {CharacterId}: it may hold at most {Max}",
                kept.Count - limit, character.Guid.Id, limit);
            kept = kept.OrderBy(k => k.Row.AppliedAt).ThenBy(k => k.Row.Slot).Take(limit).ToList();
        }

        List<ActiveAura> restored = kept.OrderBy(k => k.Row.Slot).Select(k => k.Aura).ToList();
        character.Auras.Load(restored, heldSince: now);

        if (restored.Any(a => a.Template.Modifiers.Count > 0))
            CharacterStatsRefresh.Apply(character, data, CurrentValues.EnterWorld);
    }

    /// <summary>Only a character's guid names the same unit after a restart; anything else comes back as nobody.</summary>
    private static ObjectGuid Caster(ulong saved)
    {
        var guid = new ObjectGuid(saved);
        return guid.Type == ObjectType.Character ? guid : new ObjectGuid();
    }

    private static bool Readable(float value) => float.IsFinite(value) && value >= 0f;

    /// <summary>A carried fraction is less than a whole point; anything else stored starts over at none.</summary>
    private static double Carry(double stored) => double.IsFinite(stored) && stored is >= 0d and < 1d ? stored : 0d;
}
