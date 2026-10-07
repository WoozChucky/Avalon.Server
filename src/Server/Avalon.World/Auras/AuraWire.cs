using Avalon.Network.Packets.Auras;

namespace Avalon.World.Auras;

/// <summary>A unit's auras as a client is told them (auras).</summary>
public static class AuraWire
{
    /// <summary>Every aura held, with the time each has left now; no action.</summary>
    public static List<AuraEntryDto> List(UnitAuras auras, DateTimeOffset now)
    {
        var entries = new List<AuraEntryDto>(auras.Count);
        foreach (ActiveAura aura in auras.All)
        {
            entries.Add(new AuraEntryDto
            {
                AuraId = aura.Id.Value,
                InstanceKey = aura.Key,
                CasterGuid = aura.CasterGuid.RawValue,
                Stacks = aura.Stacks,
                RemainingMs = UnitAuras.RemainingMs(aura, now),
                DurationMs = aura.DurationMs,
            });
        }

        return entries;
    }

    /// <summary>This tick's changes, in order, each as it stood when it happened.</summary>
    public static List<AuraEntryDto> Updates(IReadOnlyList<AuraChange> changes)
    {
        var entries = new List<AuraEntryDto>(changes.Count);
        for (int i = 0; i < changes.Count; i++)
        {
            AuraChange change = changes[i];
            entries.Add(new AuraEntryDto
            {
                AuraId = change.AuraId.Value,
                InstanceKey = change.Key,
                CasterGuid = change.CasterGuid,
                Stacks = change.Stacks,
                RemainingMs = change.RemainingMs,
                DurationMs = change.DurationMs,
                Action = (AuraUpdateAction)change.Kind,
            });
        }

        return entries;
    }
}
