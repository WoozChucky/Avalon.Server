using System.Collections.Immutable;

namespace Avalon.Common;

/// <summary>
/// What one equipment instance rolled (#1179): its rarity, its item level and up to <see cref="MaxModifiers" />
/// modifiers, frozen when it was made, so a later edit of the catalog or the base never re-reads them. Immutable, with
/// structural equality: two copies with the same rolls are equal. Every equipment instance has rolls; a consumable or a
/// quest item has none (null).
/// </summary>
public sealed class ItemRolls : IEquatable<ItemRolls>
{
    /// <summary>The most modifiers an instance carries: a rare's three prefixes and three suffixes.</summary>
    public const int MaxModifiers = 6;

    // Plain rolls are shared: one instance per rarity and item level, made the first time it is asked for, so a template
    // add allocates nothing. Levels past the cache (far above the level cap) get a fresh, equal instance each time.
    private const int CachedLevels = 256;
    private static readonly ItemRolls?[]?[] s_plain = new ItemRolls?[]?[4];

    public ItemRolls(ItemRarity rarity, ushort itemLevel, ReadOnlySpan<ItemModifierRoll> modifiers)
    {
        if (modifiers.Length > MaxModifiers)
        {
            throw new ArgumentException(
                $"an item carries at most {MaxModifiers} modifiers, not {modifiers.Length}", nameof(modifiers));
        }

        Rarity = rarity;
        ItemLevel = itemLevel;
        Modifiers = modifiers.IsEmpty ? ImmutableArray<ItemModifierRoll>.Empty : ImmutableArray.Create(modifiers);
    }

    public ItemRarity Rarity { get; }

    public ushort ItemLevel { get; }

    public ImmutableArray<ItemModifierRoll> Modifiers { get; }

    /// <summary>Rolls with no modifiers: a vendor's, a quest's or a starting item's. One cached instance per rarity and level.</summary>
    public static ItemRolls Plain(ItemRarity rarity, ushort itemLevel)
    {
        int index = (int)rarity;
        if ((uint)index >= (uint)s_plain.Length || itemLevel >= CachedLevels)
            return new ItemRolls(rarity, itemLevel, []);

        ItemRolls?[] levels = Volatile.Read(ref s_plain[index])
            ?? Interlocked.CompareExchange(ref s_plain[index], new ItemRolls?[CachedLevels], null)
            ?? s_plain[index]!;

        ItemRolls? cached = Volatile.Read(ref levels[itemLevel]);
        if (cached is not null)
            return cached;

        var made = new ItemRolls(rarity, itemLevel, []);
        return Interlocked.CompareExchange(ref levels[itemLevel], made, null) ?? made;
    }

    public bool Equals(ItemRolls? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (Rarity != other.Rarity || ItemLevel != other.ItemLevel || Modifiers.Length != other.Modifiers.Length)
            return false;

        for (int i = 0; i < Modifiers.Length; i++)
        {
            if (Modifiers[i] != other.Modifiers[i])
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ItemRolls other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rarity);
        hash.Add(ItemLevel);
        foreach (ItemModifierRoll modifier in Modifiers)
            hash.Add(modifier);
        return hash.ToHashCode();
    }

    public static bool operator ==(ItemRolls? left, ItemRolls? right) => Equals(left, right);

    public static bool operator !=(ItemRolls? left, ItemRolls? right) => !Equals(left, right);
}
