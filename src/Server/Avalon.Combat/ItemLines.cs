using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// One stat line of a worn item (#1179): the stat and its amount, <see cref="Min" />..<see cref="Max" /> for an "adds X to
/// Y" line and <c>Min = Max</c> for every other.
/// </summary>
public readonly record struct ItemLine(StatType Stat, int Min, int Max);

/// <summary>
/// Every stat line a worn item gives (#1179), in order: its template's stat pairs that have both a type and a value
/// (<c>Max = Min</c>), then each modifier's values in list order (<c>Min = Value</c>, <c>Max = Value2 ?? Value</c>). Read
/// by index into the template and the lists, so a <c>foreach</c> over it allocates nothing.
/// </summary>
public readonly ref struct ItemLines
{
    private readonly ItemTemplate _template;
    private readonly IReadOnlyList<ItemInstanceModifier> _modifiers;

    private ItemLines(ItemTemplate template, IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        _template = template;
        _modifiers = modifiers;
    }

    public static ItemLines Of(in WornItem item) => new(item.Template, item.Modifiers);

    public Enumerator GetEnumerator() => new(_template, _modifiers);

    public ref struct Enumerator
    {
        private const int PairCount = 10;

        private readonly ItemTemplate _template;
        private readonly IReadOnlyList<ItemInstanceModifier> _modifiers;
        private int _pair;
        private int _modifier;
        private int _value;

        internal Enumerator(ItemTemplate template, IReadOnlyList<ItemInstanceModifier> modifiers)
        {
            _template = template;
            _modifiers = modifiers;
        }

        public ItemLine Current { get; private set; }

        public bool MoveNext()
        {
            while (_pair < PairCount)
            {
                (StatType? type, uint? value) = Pair(_template, _pair++);
                if (type is { } stat && value is { } amount)
                {
                    int whole = (int)Math.Min(amount, int.MaxValue);
                    Current = new ItemLine(stat, whole, whole);
                    return true;
                }
            }

            while (_modifier < _modifiers.Count)
            {
                List<ItemInstanceModifierValue> values = _modifiers[_modifier].Values;
                if (_value < values.Count)
                {
                    ItemInstanceModifierValue line = values[_value++];
                    Current = new ItemLine(line.Stat, line.Value, line.Value2 ?? line.Value);
                    return true;
                }

                _modifier++;
                _value = 0;
            }

            return false;
        }

        private static (StatType? Type, uint? Value) Pair(ItemTemplate t, int index) => index switch
        {
            0 => (t.StatType1, t.StatValue1),
            1 => (t.StatType2, t.StatValue2),
            2 => (t.StatType3, t.StatValue3),
            3 => (t.StatType4, t.StatValue4),
            4 => (t.StatType5, t.StatValue5),
            5 => (t.StatType6, t.StatValue6),
            6 => (t.StatType7, t.StatValue7),
            7 => (t.StatType8, t.StatValue8),
            8 => (t.StatType9, t.StatValue9),
            _ => (t.StatType10, t.StatValue10),
        };
    }
}
