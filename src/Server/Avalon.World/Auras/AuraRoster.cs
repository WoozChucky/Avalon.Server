using Avalon.World.Public.Characters;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// One instance's units that hold an aura, and those whose auras changed since the last broadcast (#898), so the aura
/// pass and the broadcast walk only them rather than every character and creature here, every tick.
/// </summary>
/// <remarks>
/// <para>
/// Kept here alone. A unit is bound to the roster of the instance it is a member of: <see cref="Join" /> as it enters
/// (MapInstance.AddCharacter and AddCreature), <see cref="Leave" /> as it goes (RemoveCharacter and RemoveCreature, so a
/// despawn, a logout, Change Character, a transfer and a corpse's removal all leave through it). Its
/// <see cref="UnitAuras" /> tells the roster of every change it records (UnitAuras.Add, Changed, Remove, and Load), so
/// whatever applies or ends an aura through UnitAuras keeps the roster right without knowing of it: a dispel or a crowd
/// control has nothing to add here.
/// </para>
/// <para>
/// Characters and creatures are kept apart, so the pass and the broadcast take characters first, as they did when they
/// walked the instance's two dictionaries. Within each, a unit is in the order it became a holder here (or joined
/// holding one), and in the order it first changed this tick; the dictionaries' own order is not kept (#898).
/// </para>
/// <para>
/// A unit whose last aura ended stays listed until the next <see cref="CollectHolders" />, which drops it (it holds
/// nothing to tick, so the pass skips it either way); a unit that gains one again before then keeps its place. A unit
/// leaves both lists at once when it leaves the instance. Tick thread only, one per instance; it allocates only as its
/// lists grow.
/// </para>
/// </remarks>
public sealed class AuraRoster
{
    private readonly List<IUnit> _characterHolders = [];
    private readonly List<IUnit> _creatureHolders = [];
    private readonly List<IUnit> _changedCharacters = [];
    private readonly List<IUnit> _changedCreatures = [];

    /// <summary>The characters here whose auras changed since the last <see cref="ClearChanges" />, first changed first.</summary>
    public IReadOnlyList<IUnit> ChangedCharacters => _changedCharacters;

    /// <summary>The creatures here whose auras changed since the last <see cref="ClearChanges" />, first changed first.</summary>
    public IReadOnlyList<IUnit> ChangedCreatures => _changedCreatures;

    /// <summary>
    /// <paramref name="unit" /> is now a member of this roster's instance: listed as a holder if it holds an aura, and as
    /// changed if it carries changes in (none is sent for them: every client here is listed the unit as it comes into
    /// view; they are cleared with this tick's). Nothing for a unit that holds no auras, or that is already bound here; a
    /// unit still bound to another instance's roster leaves that one first.
    /// </summary>
    public void Join(IUnit unit)
    {
        if (AuraHolders.Of(unit) is not { } auras || ReferenceEquals(auras.Roster, this))
            return;

        auras.Roster?.Leave(unit);
        auras.Bind(this, unit);
        Noted(auras);
    }

    /// <summary>
    /// <paramref name="unit" /> is no longer a member of this roster's instance: it leaves both lists and is unbound, so
    /// nothing here ticks its auras or sends or clears its changes. Nothing for a unit not bound here.
    /// </summary>
    public void Leave(IUnit unit)
    {
        if (AuraHolders.Of(unit) is not { } auras || !ReferenceEquals(auras.Roster, this))
            return;

        if (auras.ListedAsHolder)
            RemoveFrom(unit is ICharacter ? _characterHolders : _creatureHolders, unit);
        if (auras.ListedAsChanged)
            RemoveFrom(unit is ICharacter ? _changedCharacters : _changedCreatures, unit);

        auras.ListedAsHolder = false;
        auras.ListedAsChanged = false;
        auras.Unbind();
    }

    /// <summary>
    /// Fills <paramref name="into" /> with every unit here holding an aura, characters first; false when none does. Units
    /// whose last aura has ended are dropped from the roster here. The caller walks its own copy, so an aura applied or
    /// ended during its walk never changes the list under it, and a unit that becomes a holder during it waits for the
    /// next.
    /// </summary>
    public bool CollectHolders(List<IUnit> into)
    {
        into.Clear();
        Collect(_characterHolders, into);
        Collect(_creatureHolders, into);
        return into.Count > 0;
    }

    /// <summary>Every listed unit's changes have gone to whoever sees it: forgotten, and the list emptied.</summary>
    public void ClearChanges()
    {
        Clear(_changedCharacters);
        Clear(_changedCreatures);
    }

    /// <summary>
    /// Called by <paramref name="auras" /> (bound here) after each change it records, and on joining: lists its unit as a
    /// holder once it holds an aura, and as changed once it has changes, each once.
    /// </summary>
    internal void Noted(UnitAuras auras)
    {
        if (!auras.ListedAsHolder && auras.Count > 0)
        {
            auras.ListedAsHolder = true;
            (auras.Owner is ICharacter ? _characterHolders : _creatureHolders).Add(auras.Owner!);
        }

        if (!auras.ListedAsChanged && auras.HasChanges)
        {
            auras.ListedAsChanged = true;
            (auras.Owner is ICharacter ? _changedCharacters : _changedCreatures).Add(auras.Owner!);
        }
    }

    /// <summary>Copies the holders of <paramref name="holders" /> into <paramref name="into" />, dropping in place those that hold none now.</summary>
    private static void Collect(List<IUnit> holders, List<IUnit> into)
    {
        int kept = 0;
        for (int i = 0; i < holders.Count; i++)
        {
            IUnit unit = holders[i];
            UnitAuras auras = AuraHolders.Of(unit)!;
            if (auras.Count == 0)
            {
                auras.ListedAsHolder = false;
                continue;
            }

            holders[kept++] = unit;
            into.Add(unit);
        }

        if (kept < holders.Count)
            holders.RemoveRange(kept, holders.Count - kept);
    }

    private static void Clear(List<IUnit> changed)
    {
        for (int i = 0; i < changed.Count; i++)
        {
            UnitAuras auras = AuraHolders.Of(changed[i])!;
            auras.ClearChanges();
            auras.ListedAsChanged = false;
        }

        changed.Clear();
    }

    /// <summary>By reference: a unit is in a list at most once.</summary>
    private static void RemoveFrom(List<IUnit> list, IUnit unit)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], unit))
            {
                list.RemoveAt(i);
                return;
            }
        }
    }
}
