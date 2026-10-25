using Avalon.Domain.World;
using Avalon.World.Items;

namespace Avalon.World.Characters;

/// <summary>
/// A character's active Resonance effects (#1180 part A2), as its last stats refresh built them from its counts: one slot
/// per active effect row, in the catalog's order (by theme id, then effect id), the triggers they listen for as one mask,
/// and the cooldowns they fire against. Rebuilt by a refresh only when the counts were counted again or the effect catalog
/// changed (<see cref="CharacterStatsRefresh" />), never per hit. The slots and the cooldowns are reused arrays that grow
/// only when a rebuild needs more than they hold and never shrink, so a rebuild over what the character already had room
/// for allocates nothing.
/// <para>
/// One cooldown per (theme, trigger), kept for the character's life in memory and never saved: the rows of one theme and
/// trigger at different thresholds are one effect at different strengths, so moving between them (taking a ring off and
/// putting it back) never finds a fresh cooldown, and a reload keeps them. Each firing spends its own row's cooldown.
/// Found by a scan over the entries, never by an id's value. World-side, one per character. Tick thread only.
/// </para>
/// </summary>
public sealed class ResonanceProcs
{
    // The effects the last rebuild found active, filled by the catalog and copied into the slots. Reused, so a rebuild
    // allocates nothing for it once it has held as many.
    private readonly List<ResonanceEffect> _active = [];

    private Slot[] _slots = [];
    private Cooldown[] _cooldowns = [];
    private int _cooldownCount;

    /// <summary>Every trigger an active effect listens for: an event whose bit is not here fires nothing.</summary>
    public ResonanceTriggers Mask { get; private set; }

    /// <summary>How many effects are active, in slots 0 to Count - 1.</summary>
    public int Count { get; private set; }

    /// <summary>The effect catalog the slots were last built from; <see cref="ResonanceEffectCatalog.Empty" /> before any.</summary>
    public ResonanceEffectCatalog BuiltFrom { get; private set; } = ResonanceEffectCatalog.Empty;

    /// <summary>The effect in <paramref name="slot" />.</summary>
    public ResonanceEffect EffectAt(int slot) => _slots[Checked(slot)].Effect;

    /// <summary>When the effect in <paramref name="slot" /> may fire again; <c>default</c> when it never has.</summary>
    public DateTime ReadyAt(int slot) => _cooldowns[_slots[Checked(slot)].Cooldown].ReadyAt;

    /// <summary>The effect in <paramref name="slot" /> fired: it, and every row of its theme and trigger, waits until <paramref name="readyAt" />.</summary>
    public void Spend(int slot, DateTime readyAt) => _cooldowns[_slots[Checked(slot)].Cooldown].ReadyAt = readyAt;

    /// <summary>
    /// The slots and mask become the effects <paramref name="effects" /> makes active for <paramref name="counts" /> (for
    /// each theme, the rows of the highest threshold its count reaches), each bound to its theme and trigger's cooldown,
    /// which is added the first time the character has that pair active. Slots no longer active are let go.
    /// </summary>
    public void Rebuild(ResonanceCounts counts, ResonanceEffectCatalog effects)
    {
        List<ResonanceEffect> active = _active;
        active.Clear();
        int count = effects.ActiveFor(counts, active);
        if (_slots.Length < count)
            Array.Resize(ref _slots, count);

        ResonanceTriggers mask = ResonanceTriggers.None;
        for (int i = 0; i < count; i++)
        {
            ResonanceEffect effect = active[i];
            _slots[i] = new Slot(effect, CooldownOf(effect.ThemeId, effect.Trigger));
            mask |= effect.Bit;
        }

        // The effects of slots no longer active, and the list's, are let go, so an old catalog is not held alive by them.
        Array.Clear(_slots, count, _slots.Length - count);
        active.Clear();

        Count = count;
        Mask = mask;
        BuiltFrom = effects;
    }

    /// <summary>The index of the cooldown of that theme and trigger, added (ready) when the character has none yet.</summary>
    private int CooldownOf(int themeId, ResonanceTrigger trigger)
    {
        for (int i = 0; i < _cooldownCount; i++)
        {
            if (_cooldowns[i].ThemeId == themeId && _cooldowns[i].Trigger == trigger)
                return i;
        }

        if (_cooldownCount == _cooldowns.Length)
            Array.Resize(ref _cooldowns, Math.Max(4, _cooldowns.Length * 2));

        _cooldowns[_cooldownCount] = new Cooldown(themeId, trigger, default);
        return _cooldownCount++;
    }

    private int Checked(int slot) =>
        (uint)slot < (uint)Count ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, null);

    /// <summary>An active effect and the index of its cooldown, which stays valid for the character's life.</summary>
    private readonly record struct Slot(ResonanceEffect Effect, int Cooldown);

    private record struct Cooldown(int ThemeId, ResonanceTrigger Trigger, DateTime ReadyAt);
}
