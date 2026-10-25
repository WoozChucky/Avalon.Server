using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
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
        CharacterClass Class = CharacterClass.Warrior,
        bool Rolled = false);

    private static (SlotRef, ItemTemplate, uint) At(SlotRef slot, ItemTemplate template, uint count = 1) => (slot, template, count);

    private static readonly MoveCase[] s_cases =
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
        new("Wear an off hand beside a one-hander", [At(Eq(EquipmentSlots.MainHand), Longsword), At(Bag(0), Buckler)],
            Bag(0), Eq(EquipmentSlots.OffHand), null, ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Wear a one-hander while the off hand is held", [At(Eq(EquipmentSlots.OffHand), Buckler), At(Bag(0), Longsword)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Ok, MoveKind.Move, 1),

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
        new("Move an item whose template is gone within the bag", [At(Bag(0), Ghost)], Bag(0), Bag(1), null,
            ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Take off an item whose template is gone", [At(Eq(EquipmentSlots.Finger2), Ghost)], Eq(EquipmentSlots.Finger2), Bag(0), null,
            ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Deposit an item whose template is gone", [At(Bag(0), Ghost)], Bag(0), Vault(0), null,
            ItemRequestResult.Ok, MoveKind.Move, 1, Bank: true),
        new("Refuse an item whose template is gone into equipment", [At(Bag(0), Ghost)], Bag(0), Eq(EquipmentSlots.Head), null,
            ItemRequestResult.WrongEquipSlot),
        new("Refuse a split of an item whose template is gone", [At(Bag(0), Ghost, 3)], Bag(0), Bag(1), 1,
            ItemRequestResult.NotStackable),
        new("Refuse a swap that would wear an item whose template is gone", [At(Eq(EquipmentSlots.MainHand), Longsword), At(Bag(0), Ghost)],
            Eq(EquipmentSlots.MainHand), Bag(0), null, ItemRequestResult.WrongEquipSlot),
        new("Refuse a slot past the container", [At(Bag(0), Potion, 5)], Bag(0), Bag(30), null, ItemRequestResult.InvalidSlot),
        new("Refuse the same slot twice", [At(Bag(0), Potion, 5)], Bag(0), Bag(0), null, ItemRequestResult.InvalidSlot),
        new("Refuse a reserved equipment slot as the target", [At(Bag(0), Band)], Bag(0), Eq(13), null,
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
        new("Wear gear with no class list as a Wizard", [At(Bag(0), Band)], Bag(0), Eq(EquipmentSlots.Finger1), null,
            ItemRequestResult.Ok, MoveKind.Move, 1, Class: CharacterClass.Wizard),
        new("Refuse a split into equipment", [At(Bag(0), StackedBand, 3)], Bag(0), Eq(EquipmentSlots.Finger1), 1,
            ItemRequestResult.NotStackable),
        new("Refuse a split of an item that does not stack", [At(Bag(0), Sword, 2)], Bag(0), Bag(1), 1,
            ItemRequestResult.NotStackable),
        new("Swap two copies of an item that does not stack", [At(Bag(0), Sword), At(Bag(1), Sword)], Bag(0), Bag(1), null,
            ItemRequestResult.Ok, MoveKind.Swap, 1),
        new("Swap a worn weapon for a spare copy of it", [At(Eq(EquipmentSlots.MainHand), Longsword), At(Bag(0), Longsword)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Ok, MoveKind.Swap, 1),
        new("Refuse a stack into equipment", [At(Bag(0), StackedBand, 3)], Bag(0), Eq(EquipmentSlots.Finger1), null,
            ItemRequestResult.NotStackable),
        new("Refuse swapping a stack into equipment", [At(Eq(EquipmentSlots.Finger1), Band), At(Bag(0), StackedBand, 3)],
            Bag(0), Eq(EquipmentSlots.Finger1), null, ItemRequestResult.NotStackable),
        new("Refuse a swap that would bring a stack into equipment", [At(Eq(EquipmentSlots.Finger1), Band), At(Bag(0), StackedBand, 3)],
            Eq(EquipmentSlots.Finger1), Bag(0), null, ItemRequestResult.NotStackable),
        new("Move in the bag while an old row wears a two-hander and an off hand",
            [At(Eq(EquipmentSlots.MainHand), Greatsword), At(Eq(EquipmentSlots.OffHand), Buckler), At(Bag(0), Potion, 5)],
            Bag(0), Bag(1), null, ItemRequestResult.Ok, MoveKind.Move, 5),
        new("Refuse a merge into equipment", [At(Bag(0), StackedBand, 2), At(Eq(EquipmentSlots.Finger1), StackedBand, 1)],
            Bag(0), Eq(EquipmentSlots.Finger1), null, ItemRequestResult.NotStackable),
        new("Refuse a merge onto a full stack", [At(Bag(0), Potion, 5), At(Bag(1), Potion, 20)], Bag(0), Bag(1), null,
            ItemRequestResult.TargetFull),
        // #1179: an item carrying modifiers is one of a kind, whatever its template allows.
        new("Refuse splitting a stackable item that carries modifiers", [At(Bag(0), Potion, 5)], Bag(0), Bag(3), 2,
            ItemRequestResult.NotStackable, Rolled: true),
        new("Refuse a two-hander while the off hand is held", [At(Eq(EquipmentSlots.OffHand), Buckler), At(Bag(0), Greatsword)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Blocked),
        new("Refuse a bow while the off hand is held", [At(Eq(EquipmentSlots.OffHand), Buckler), At(Bag(0), Shortbow)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Blocked),
        new("Refuse an off hand while a two-hander is worn", [At(Eq(EquipmentSlots.MainHand), Greatsword), At(Bag(0), Buckler)],
            Bag(0), Eq(EquipmentSlots.OffHand), null, ItemRequestResult.Blocked),
        new("Refuse swapping a two-hander in while the off hand is held",
            [At(Eq(EquipmentSlots.MainHand), Longsword), At(Eq(EquipmentSlots.OffHand), Buckler), At(Bag(0), Greatsword)],
            Bag(0), Eq(EquipmentSlots.MainHand), null, ItemRequestResult.Blocked),
        // #1179: two copies of a template may be worn, unless it is UniqueEquipped; then the one worn may still move or be
        // swapped for another copy.
        new("Refuse a second copy of a UniqueEquipped trinket", [At(Eq(EquipmentSlots.Trinket1), WardStone), At(Bag(0), WardStone)],
            Bag(0), Eq(EquipmentSlots.Trinket2), null, ItemRequestResult.UniqueEquipped),
        new("Wear a second copy of a trinket that is not UniqueEquipped", [At(Eq(EquipmentSlots.Trinket1), Talisman), At(Bag(0), Talisman)],
            Bag(0), Eq(EquipmentSlots.Trinket2), null, ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Move a worn UniqueEquipped trinket to the other trinket slot", [At(Eq(EquipmentSlots.Trinket1), WardStone)],
            Eq(EquipmentSlots.Trinket1), Eq(EquipmentSlots.Trinket2), null, ItemRequestResult.Ok, MoveKind.Move, 1),
        new("Swap a worn UniqueEquipped trinket for another copy", [At(Eq(EquipmentSlots.Trinket1), WardStone), At(Bag(0), WardStone)],
            Bag(0), Eq(EquipmentSlots.Trinket1), null, ItemRequestResult.Ok, MoveKind.Swap, 1),
        new("Refuse a swap that would put a bag item in the wrong equipment slot",
            [At(Eq(EquipmentSlots.MainHand), Longsword), At(Bag(0), Potion)],
            Eq(EquipmentSlots.MainHand), Bag(0), null, ItemRequestResult.WrongEquipSlot),
    ];

    public static IEnumerable<object[]> CaseNames => s_cases.Select(c => new object[] { c.Name });

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

        if (row.Rolled && character.Container(row.From.Container).TryGet(row.From.Slot, out InventoryItem rolled))
        {
            character.ItemModifiers.Load([new ItemInstance
            {
                Identified = true,
                Id = rolled.InstanceId,
                TemplateId = rolled.TemplateId,
                Modifiers = [TestModifiers.Modifier(rolled.InstanceId, 1, 0, (0, StatType.Health, 10, null))],
            }]);
        }

        return character;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Decide_Every_Row_Of_The_Move_Table(string name)
    {
        MoveCase row = s_cases.Single(c => c.Name == name);
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

    /// <summary>
    /// R13: the requirements read the attributes of the last stats refresh, gear worn included, so the item a swap takes
    /// off still counts. A row of 0s has had no refresh yet (Stats null), which reads as 0s.
    /// </summary>
    [Theory]
    [InlineData((ushort)25, null, null, 23u, 20u, 20u, ItemRequestResult.AttributesTooLow)]
    [InlineData((ushort)23, null, null, 23u, 20u, 20u, ItemRequestResult.Ok)]
    [InlineData(null, (ushort)21, (ushort)20, 23u, 20u, 20u, ItemRequestResult.AttributesTooLow)]
    [InlineData(null, null, null, 0u, 0u, 0u, ItemRequestResult.Ok)]
    [InlineData((ushort)1, null, null, 0u, 0u, 0u, ItemRequestResult.AttributesTooLow)]   // no refresh yet: any requirement refuses
    [InlineData((ushort)25, null, null, 25u, 20u, 20u, ItemRequestResult.Ok, true)]   // 25 only with the worn item it replaces: attributes now count
    public void Equip_only_when_every_attribute_requirement_is_met(ushort? str, ushort? agi, ushort? intl,
        uint hasStr, uint hasAgi, uint hasInt, ItemRequestResult expected, bool replacesWorn = false)
    {
        var demanding = new ItemTemplate
        {
            Id = new ItemTemplateId(700),
            Name = "Demanding Helm",
            Class = ItemClass.Armor,
            SubClass = ItemSubClass.Helmet,
            MaxStackSize = 1,
            Slot = ItemSlotType.Head,
            RequiredStrength = str,
            RequiredAgility = agi,
            RequiredIntellect = intl,
        };
        var worn = new ItemTemplate
        {
            Id = new ItemTemplateId(701),
            Name = "Worn Helm",
            Class = ItemClass.Armor,
            SubClass = ItemSubClass.Helmet,
            MaxStackSize = 1,
            Slot = ItemSlotType.Head,
            StatType1 = StatType.Strength,
            StatValue1 = 2,
        };
        ItemTemplate? Find(ItemTemplateId id) => id == demanding.Id ? demanding : id == worn.Id ? worn : null;

        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, demanding)]);
        if (replacesWorn)
            character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Head, worn)]);
        if (hasStr + hasAgi + hasInt > 0)
        {
            character.ApplyStats(new DerivedCharacterStats(100, 50, 10, hasStr, hasAgi, hasInt, 0, 0f, 0, 0f, 0, 0),
                CurrentValues.EnterWorld, TestCombat.Formula);
        }

        MoveDecision decision = InventoryMove.Decide(character, Find, bankAccessible: false, Bag(0), Eq(EquipmentSlots.Head), null);

        Assert.Equal(expected, decision.Result);
    }

    /// <summary>
    /// #1179: a drag puts an unidentified copy into no equipment slot, an empty one or one whose worn item it would swap
    /// out, and a refusal leaves every slot as it was; it can still be banked.
    /// </summary>
    [Theory]
    [InlineData("move to its slot", ItemRequestResult.Unidentified)]
    [InlineData("swap with the worn chest", ItemRequestResult.Unidentified)]
    [InlineData("deposit in the bank", ItemRequestResult.Ok)]
    public void Wear_only_an_identified_item_by_drag(string route, ItemRequestResult expected)
    {
        ItemTemplate chest = ItemBaseSeed.Equipment().Single(t => t.Name == "Ironbound Chestguard");
        ItemTemplate? Find(ItemTemplateId id) => id == chest.Id ? chest : EquipTemplates.Find(id);
        CharacterEntity character = New();
        InventoryItem unidentified = Item(0, chest, identified: false) with { Rarity = ItemRarity.Magic };
        InventoryItem worn = Item(EquipmentSlots.Chest, chest);
        character.Container(InventoryType.Bag).Load([unidentified]);
        if (route == "swap with the worn chest")
            character.Container(InventoryType.Equipment).Load([worn]);
        SlotRef to = route == "deposit in the bank" ? new SlotRef(InventoryType.Bank, 0) : Eq(EquipmentSlots.Chest);

        Assert.Equal(expected, InventoryFor(character, Find).TryMove(Bag(0), to, count: null, bankAccessible: true));

        if (expected == ItemRequestResult.Ok)
        {
            Assert.Equal(unidentified, TestCharacters.At(character, InventoryType.Bank, 0));
            return;
        }

        Assert.Equal(unidentified, TestCharacters.At(character, InventoryType.Bag, 0));
        Assert.Equal(route == "swap with the worn chest" ? new[] { worn } : [], character.Container(InventoryType.Equipment).Items);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    public static IEnumerable<object?[]> DestroyCases =>
    [
        ["Destroy a whole stack", At(Bag(0), Potion, 5), Bag(0), null, false, ItemRequestResult.Ok, 5u],
        ["Destroy part of a stack", At(Bag(0), Potion, 5), Bag(0), 2u, false, ItemRequestResult.Ok, 2u],
        ["Destroy a worn item", At(Eq(EquipmentSlots.MainHand), Longsword), Eq(EquipmentSlots.MainHand), null, false, ItemRequestResult.Ok, 1u],
        ["Destroy in an open bank", At(Vault(4), Potion, 5), Vault(4), null, true, ItemRequestResult.Ok, 5u],
        ["Refuse an empty slot", At(Bag(1), Potion, 5), Bag(0), null, false, ItemRequestResult.NotFound, 0u],
        ["Destroy an item whose template is gone", At(Bag(0), Ghost, 3), Bag(0), null, false, ItemRequestResult.Ok, 3u],
        ["Refuse a count of zero", At(Bag(0), Potion, 5), Bag(0), 0u, false, ItemRequestResult.InvalidCount, 0u],
        ["Refuse a count above the stack", At(Bag(0), Potion, 5), Bag(0), 6u, false, ItemRequestResult.InvalidCount, 0u],
        ["Refuse an item that cannot be destroyed", At(Bag(0), Heirloom), Bag(0), null, false, ItemRequestResult.CannotDestroy, 0u],
        ["Refuse the bank while it is closed", At(Vault(4), Potion, 5), Vault(4), null, false, ItemRequestResult.BankClosed, 0u],
        ["Refuse a reserved equipment slot", At(Bag(0), Potion, 5), Eq(13), null, false, ItemRequestResult.InvalidSlot, 0u],
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
