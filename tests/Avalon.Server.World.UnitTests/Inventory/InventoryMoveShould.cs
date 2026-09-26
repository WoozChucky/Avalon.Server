using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using static Avalon.Server.World.UnitTests.Inventory.EquipTemplates;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// The spec's move table, one row per line, and every refusal. InventoryMove only decides: every
/// row also proves it changed nothing.
/// </summary>
public class InventoryMoveShould
{
    public sealed record MoveCase(
        string Name,
        (SlotRef At, ItemTemplate Template, uint Count)[] Held,
        SlotRef From,
        SlotRef To,
        uint? Count,
        ItemRequestResult Expected,
        MoveKind? Kind = null,
        uint PlanCount = 0,
        bool Bank = false,
        ushort Level = 1,
        CharacterClass Class = CharacterClass.Warrior);

    private static (SlotRef, ItemTemplate, uint) At(SlotRef slot, ItemTemplate template, uint count = 1) => (slot, template, count);

    private static readonly MoveCase[] Cases =
    [
        // The rule table.
        new("Move a whole stack to an empty slot", [At(Bag(0), Potion, 5)], Bag(0), Bag(3), null,
            ItemRequestResult.Ok, MoveKind.Move, 5),
        new("Move when the count names the whole stack", [At(Bag(0), Potion, 5)], Bag(0), Bag(3), 5,
            ItemRequestResult.Ok, MoveKind.Move, 5),
        new("Split part of a stack into an empty slot", [At(Bag(0), Potion, 5)], Bag(0), Bag(3), 2,
            ItemRequestResult.Ok, MoveKind.Split, 2),
        new("Merge a whole stack onto the same item", [At(Bag(0), Potion, 5), At(Bag(1), Potion, 10)], Bag(0), Bag(1), null,
            ItemRequestResult.Ok, MoveKind.Merge, 5),
        new("Merge only up to the stack size", [At(Bag(0), Potion, 15), At(Bag(1), Potion, 10)], Bag(0), Bag(1), null,
            ItemRequestResult.Ok, MoveKind.Merge, 10),
        new("Merge part of a stack", [At(Bag(0), Potion, 15), At(Bag(1), Potion, 10)], Bag(0), Bag(1), 3,
            ItemRequestResult.Ok, MoveKind.Merge, 3),
        new("Swap two different items", [At(Bag(0), Potion, 5), At(Bag(1), Sword)], Bag(0), Bag(1), null,
            ItemRequestResult.Ok, MoveKind.Swap, 5),
        new("Refuse part of a stack aimed at a different item", [At(Bag(0), Potion, 5), At(Bag(1), Sword)], Bag(0), Bag(1), 2,
            ItemRequestResult.InvalidCount),

        // Equipment.
        new("Equip a weapon into the main hand", [At(Bag(0), Longsword)], Bag(0), Eq(EquipmentSlots.MainHand), null,
            ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Unequip into an empty bag slot", [At(Eq(EquipmentSlots.MainHand), Longsword)], Eq(EquipmentSlots.MainHand), Bag(0), null,
            ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Swap a bag weapon with the one worn", [At(Eq(EquipmentSlots.MainHand), Longsword), At(Bag(0), Axe)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Ok, MoveKind.Swap, 1),
        new("Wear a ring on the second finger", [At(Bag(0), Band)], Bag(0), Eq(EquipmentSlots.Finger2), null,
            ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Take a gem off the second finger slot", [At(Eq(EquipmentSlots.Finger2), Ruby)], Eq(EquipmentSlots.Finger2), Bag(0), null,
            ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Wear gear at exactly its level", [At(Bag(0), IronHelm)], Bag(0), Eq(EquipmentSlots.Head), null,
            ItemRequestResult.Ok, MoveKind.Move, 1, Level: 3),
        new("Wear a two-hander with the off hand empty", [At(Bag(0), Greatsword)], Bag(0), Eq(EquipmentSlots.MainHand), null,
            ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Take the off hand off while a two-hander is worn",
            [At(Eq(EquipmentSlots.MainHand), Greatsword), At(Eq(EquipmentSlots.OffHand), Buckler)],
            Eq(EquipmentSlots.OffHand), Bag(0), null, ItemRequestResult.Ok, MoveKind.Move, 1),

        // The Bank.
        new("Deposit into an open bank", [At(Bag(0), Potion, 5)], Bag(0), Vault(0), null,
            ItemRequestResult.Ok, MoveKind.Move, 5, Bank: true),
        new("Withdraw from an open bank", [At(Vault(0), Potion, 5)], Vault(0), Bag(0), null,
            ItemRequestResult.Ok, MoveKind.Move, 5, Bank: true),
        new("Refuse a deposit while the bank is closed", [At(Bag(0), Potion, 5)], Bag(0), Vault(0), null,
            ItemRequestResult.BankClosed),
        new("Refuse a withdrawal while the bank is closed", [At(Vault(0), Potion, 5)], Vault(0), Bag(0), null,
            ItemRequestResult.BankClosed),
        new("Check the slot range before the bank", [At(Bag(0), Potion, 5)], Bag(0), Vault(30), null,
            ItemRequestResult.InvalidSlot),

        // Refusals.
        new("Refuse an empty source", [], Bag(0), Bag(1), null, ItemRequestResult.NotFound),
        new("Refuse an item whose template is gone", [At(Bag(0), Ghost)], Bag(0), Bag(1), null, ItemRequestResult.NotFound),
        new("Refuse a slot past the container", [At(Bag(0), Potion, 5)], Bag(0), Bag(30), null, ItemRequestResult.InvalidSlot),
        new("Refuse the same slot twice", [At(Bag(0), Potion, 5)], Bag(0), Bag(0), null, ItemRequestResult.InvalidSlot),
        new("Refuse a reserved equipment slot as the target", [At(Bag(0), Band)], Bag(0), Eq(11), null,
            ItemRequestResult.InvalidSlot),
        new("Refuse a reserved equipment slot as the source", [], Eq(13), Bag(0), null, ItemRequestResult.InvalidSlot),
        new("Refuse a count of zero", [At(Bag(0), Potion, 5)], Bag(0), Bag(1), 0, ItemRequestResult.InvalidCount),
        new("Refuse a count above the stack", [At(Bag(0), Potion, 5)], Bag(0), Bag(1), 6, ItemRequestResult.InvalidCount),
        new("Refuse gear aimed at the wrong equipment slot", [At(Bag(0), IronHelm)], Bag(0), Eq(EquipmentSlots.MainHand), null,
            ItemRequestResult.WrongEquipSlot),
        new("Refuse an item with no equipment slot", [At(Bag(0), Potion)], Bag(0), Eq(EquipmentSlots.Head), null,
            ItemRequestResult.WrongEquipSlot),
        new("Refuse a gem in the second finger slot", [At(Bag(0), Ruby)], Bag(0), Eq(EquipmentSlots.Finger2), null,
            ItemRequestResult.WrongEquipSlot),
        new("Refuse gear above the character's level", [At(Bag(0), IronHelm)], Bag(0), Eq(EquipmentSlots.Head), null,
            ItemRequestResult.LevelTooLow),
        new("Refuse gear for another class", [At(Bag(0), Circlet)], Bag(0), Eq(EquipmentSlots.Head), null,
            ItemRequestResult.WrongClass),
        new("Wear another class's gear as that class", [At(Bag(0), Circlet)], Bag(0), Eq(EquipmentSlots.Head), null,
            ItemRequestResult.Ok, MoveKind.Move, 1, Class: CharacterClass.Wizard),
        new("Refuse a split into equipment", [At(Bag(0), StackedBand, 3)], Bag(0), Eq(EquipmentSlots.Finger1), 1,
            ItemRequestResult.NotStackable),
        new("Refuse a split of an item that does not stack", [At(Bag(0), Sword, 2)], Bag(0), Bag(1), 1,
            ItemRequestResult.NotStackable),
        new("Refuse a merge of an item that does not stack", [At(Bag(0), Sword), At(Bag(1), Sword)], Bag(0), Bag(1), null,
            ItemRequestResult.NotStackable),
        new("Refuse a merge into equipment", [At(Bag(0), StackedBand, 2), At(Eq(EquipmentSlots.Finger1), StackedBand, 1)],
            Bag(0), Eq(EquipmentSlots.Finger1), null, ItemRequestResult.NotStackable),
        new("Refuse a merge onto a full stack", [At(Bag(0), Potion, 5), At(Bag(1), Potion, 20)], Bag(0), Bag(1), null,
            ItemRequestResult.TargetFull),
        new("Refuse a two-hander while the off hand is held", [At(Eq(EquipmentSlots.OffHand), Buckler), At(Bag(0), Greatsword)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Blocked),
        new("Refuse an off hand while a two-hander is worn", [At(Eq(EquipmentSlots.MainHand), Greatsword), At(Bag(0), Buckler)],
            Bag(0), Eq(EquipmentSlots.OffHand), null, ItemRequestResult.Blocked),
        new("Refuse swapping a two-hander in while the off hand is held",
            [At(Eq(EquipmentSlots.MainHand), Longsword), At(Eq(EquipmentSlots.OffHand), Buckler), At(Bag(0), Greatsword)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Blocked),
        new("Refuse a swap that would put a bag item in the wrong equipment slot",
            [At(Eq(EquipmentSlots.MainHand), Longsword), At(Bag(0), Potion)],
            Eq(EquipmentSlots.MainHand), Bag(0), null, ItemRequestResult.WrongEquipSlot),
    ];

    public static IEnumerable<object[]> CaseNames => Cases.Select(c => new object[] { c.Name });

    private static CharacterEntity Arrange(MoveCase row)
    {
        CharacterEntity character = New();
        character.Data!.Class = row.Class;
        character.Level = row.Level;

        foreach (IGrouping<InventoryType, (SlotRef At, ItemTemplate Template, uint Count)> container in
                 row.Held.GroupBy(h => h.At.Container))
        {
            character.Container(container.Key).Load(
                container.Select(h => Item(h.At.Slot, h.Template, h.Count)).ToList());
        }

        return character;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Decide_Every_Row_Of_The_Move_Table(string name)
    {
        MoveCase row = Cases.Single(c => c.Name == name);
        CharacterEntity character = Arrange(row);

        MoveDecision decision = InventoryMove.Decide(character, EquipTemplates.Find, row.Bank, row.From, row.To, row.Count);

        Assert.Equal(row.Expected, decision.Result);
        if (row.Kind is { } kind)
        {
            Assert.True(decision.Accepted);
            Assert.Equal(new MovePlan(kind, row.From, row.To, row.PlanCount), decision.Plan);
        }
        else
        {
            Assert.False(decision.Accepted);
        }

        // Pure: deciding marks nothing and moves nothing.
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
        Assert.Equal(row.Held.Length,
            character.Container(InventoryType.Equipment).Items.Count
            + character.Container(InventoryType.Bag).Items.Count
            + character.Container(InventoryType.Bank).Items.Count);
    }

    public static IEnumerable<object?[]> DestroyCases =>
    [
        ["Destroy a whole stack", At(Bag(0), Potion, 5), Bag(0), null, false, ItemRequestResult.Ok, 5u],
        ["Destroy part of a stack", At(Bag(0), Potion, 5), Bag(0), 2u, false, ItemRequestResult.Ok, 2u],
        ["Destroy a worn item", At(Eq(EquipmentSlots.MainHand), Longsword), Eq(EquipmentSlots.MainHand), null, false, ItemRequestResult.Ok, 1u],
        ["Destroy in an open bank", At(Vault(4), Potion, 5), Vault(4), null, true, ItemRequestResult.Ok, 5u],
        ["Refuse an empty slot", At(Bag(1), Potion, 5), Bag(0), null, false, ItemRequestResult.NotFound, 0u],
        ["Refuse an item whose template is gone", At(Bag(0), Ghost), Bag(0), null, false, ItemRequestResult.NotFound, 0u],
        ["Refuse a count of zero", At(Bag(0), Potion, 5), Bag(0), 0u, false, ItemRequestResult.InvalidCount, 0u],
        ["Refuse a count above the stack", At(Bag(0), Potion, 5), Bag(0), 6u, false, ItemRequestResult.InvalidCount, 0u],
        ["Refuse an item that cannot be destroyed", At(Bag(0), Heirloom), Bag(0), null, false, ItemRequestResult.CannotDestroy, 0u],
        ["Refuse the bank while it is closed", At(Vault(4), Potion, 5), Vault(4), null, false, ItemRequestResult.BankClosed, 0u],
        ["Refuse a reserved equipment slot", At(Bag(0), Potion, 5), Eq(12), null, false, ItemRequestResult.InvalidSlot, 0u],
        ["Refuse a slot past the container", At(Bag(0), Potion, 5), Bag(30), null, false, ItemRequestResult.InvalidSlot, 0u],
    ];

    [Theory]
    [MemberData(nameof(DestroyCases))]
    public void Decide_Every_Destroy(string name, (SlotRef At, ItemTemplate Template, uint Count) held, SlotRef slot,
        uint? count, bool bank, ItemRequestResult expected, uint destroying)
    {
        _ = name;
        CharacterEntity character = New();
        character.Container(held.At.Container).Load([Item(held.At.Slot, held.Template, held.Count)]);

        ItemRequestResult result = InventoryMove.DecideDestroy(character, EquipTemplates.Find, bank, slot, count, out uint decided);

        Assert.Equal(expected, result);
        Assert.Equal(destroying, decided);
        Assert.False(character.SaveState.HasChanges);
    }
}
