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
/// At select: the saved auras resume as they were, their time having stood still while the character was out of the
/// world; each keeps its snapshot, stacks, ticks owed, carried fraction of a point and caster, and is held under the
/// template loaded now. A row whose aura is no longer loaded is dropped with a warning. Restoring owes the client
/// nothing: it gets a list when the character enters the world. A restored stat aura refills the pools as select does.
/// </summary>
public static class AuraRestore
{
    public static void Restore(CharacterEntity character, IReadOnlyCollection<CharacterAura> rows, StaticData data,
        DateTimeOffset now, ILogger logger)
    {
        var restored = new List<ActiveAura>(rows.Count);
        foreach (CharacterAura row in rows.OrderBy(r => r.Slot))
        {
            if (!data.Auras.TryGet(new AuraId(row.AuraId), out AuraTemplate? template))
            {
                logger.LogWarning("Dropped the saved aura {AuraId} of character {CharacterId}: it is not loaded",
                    row.AuraId, character.Guid.Id);
                continue;
            }

            if (row.RemainingMs == 0)
                continue;

            AuraSource source = row.SourceAbilityId is { } abilityId &&
                                data.Abilities.TryGet(new AbilityId(abilityId), out AbilityTemplate? ability)
                ? AuraSource.Of(ability)
                : AuraSource.None;

            restored.Add(new ActiveAura(template, new ObjectGuid(row.CasterGuid), source, (uint)Math.Max(1, row.Stacks),
                new AuraSnapshot(row.TickAmount, row.CritPct, (ushort)Math.Clamp(row.CasterLevel, 0, ushort.MaxValue)),
                AuraSchedule.Resume(now, row.RemainingMs, template.TickIntervalMs, row.TicksLeft), row.DurationMs,
                row.AppliedAt)
            {
                PeriodicCarry = Carry(row.PeriodicCarry),
            });
        }

        character.Auras.Load(restored);

        if (restored.Any(a => a.Template.Modifiers.Count > 0))
            CharacterStatsRefresh.Apply(character, data, CurrentValues.EnterWorld);
    }

    /// <summary>A carried fraction is less than a whole point; anything else stored starts over at none.</summary>
    private static double Carry(double stored) => double.IsFinite(stored) && stored is >= 0d and < 1d ? stored : 0d;
}
