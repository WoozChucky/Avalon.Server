namespace Avalon.Domain.World;

/// <summary>
/// A copy's rolled base stats in memory (#1179): one value, so they travel by copy through the roll, the store, a save,
/// a trade and the stats fold without an allocation or a deep copy. A stat the copy has no row for is null, never 0.
/// They convert to and from rows only at load (<see cref="Of" />) and at write (<see cref="GetEnumerator" />).
/// </summary>
public readonly record struct ItemBaseStats(
    int? Armour, int? Evasion, int? Ward, int? BlockChance, int? DamageMin, int? DamageMax, int? AttackTime)
{
    /// <summary>The value of <paramref name="stat" />, or null when the copy has none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value that names no base stat.</exception>
    public int? this[ItemBaseStat stat] => stat switch
    {
        ItemBaseStat.Armour => Armour,
        ItemBaseStat.Evasion => Evasion,
        ItemBaseStat.Ward => Ward,
        ItemBaseStat.BlockChance => BlockChance,
        ItemBaseStat.DamageMin => DamageMin,
        ItemBaseStat.DamageMax => DamageMax,
        ItemBaseStat.AttackTime => AttackTime,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };

    /// <summary>True when the copy has no base stat at all (jewellery, a trinket, an item that is no base).</summary>
    public bool IsEmpty => this == default;

    /// <summary>These base stats with <paramref name="stat" /> set to <paramref name="value" />.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value that names no base stat.</exception>
    public ItemBaseStats With(ItemBaseStat stat, int value) => stat switch
    {
        ItemBaseStat.Armour => this with { Armour = value },
        ItemBaseStat.Evasion => this with { Evasion = value },
        ItemBaseStat.Ward => this with { Ward = value },
        ItemBaseStat.BlockChance => this with { BlockChance = value },
        ItemBaseStat.DamageMin => this with { DamageMin = value },
        ItemBaseStat.DamageMax => this with { DamageMax = value },
        ItemBaseStat.AttackTime => this with { AttackTime = value },
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };

    /// <summary>The base stats a copy's rows hold; a stat with no row stays null.</summary>
    public static ItemBaseStats Of(IEnumerable<ItemInstanceBaseStat> rows)
    {
        ItemBaseStats stats = default;
        foreach (ItemInstanceBaseStat row in rows)
            stats = stats.With(row.Stat, row.Value);

        return stats;
    }

    /// <summary>Every stat the copy has, in <see cref="ItemBaseStat" /> order, without allocating.</summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Walks the set members of an <see cref="ItemBaseStats" /> in <see cref="ItemBaseStat" /> order.</summary>
    public struct Enumerator
    {
        private const int Count = (int)ItemBaseStat.AttackTime + 1;

        private readonly ItemBaseStats _stats;
        private int _next;

        internal Enumerator(ItemBaseStats stats)
        {
            _stats = stats;
            _next = 0;
            Current = default;
        }

        public (ItemBaseStat Stat, int Value) Current { get; private set; }

        public bool MoveNext()
        {
            while (_next < Count)
            {
                var stat = (ItemBaseStat)_next++;
                if (_stats[stat] is { } value)
                {
                    Current = (stat, value);
                    return true;
                }
            }

            return false;
        }
    }
}
