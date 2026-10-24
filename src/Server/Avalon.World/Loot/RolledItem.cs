using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.Loot;

/// <summary>
/// One copy of an item as a roll made it (#1179): its own id, its template, rarity and item level, a rare's stored name,
/// and its modifiers in ordinal order, each already naming the copy and carrying its rolled values. Everything is
/// decided at the roll, so whoever adds the copy only puts it somewhere. The roll made <see cref="Modifiers" /> for this
/// copy alone and keeps no reference to it: the list is handed to the character's modifier store as the copy's own entry
/// (ItemModifierStore.PutMade), never copied, and nothing else may change it.
/// </summary>
public sealed record RolledItem(
    ItemInstanceId InstanceId,
    ItemTemplateId TemplateId,
    ItemRarity Rarity,
    ushort ItemLevel,
    string? RareName,
    List<ItemInstanceModifier> Modifiers);
