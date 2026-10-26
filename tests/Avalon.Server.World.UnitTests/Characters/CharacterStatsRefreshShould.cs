using System.Globalization;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>Writing derived stats to the character, and working out what it is wearing.</summary>
public class CharacterStatsRefreshShould
{
    private static readonly ClassLevelStat s_warriorLevel1 = new()
    {
        Class = CharacterClass.Warrior,
        Level = 1,
        BaseHp = 130,
        BaseMana = 0,
        Stamina = 22,
        Strength = 23,
        Agility = 20,
        Intellect = 20,
    };

    private static readonly ClassLevelStat s_wizardLevel1 = new()
    {
        Class = CharacterClass.Wizard,
        Level = 1,
        BaseHp = 16,
        BaseMana = 20,
        Stamina = 21,
        Strength = 20,
        Agility = 20,
        Intellect = 23,
    };

    private static readonly ClassLevelStat s_hunterLevel1 = new()
    {
        Class = CharacterClass.Hunter,
        Level = 1,
        BaseHp = 78,
        BaseMana = 10,
        Stamina = 20,
        Strength = 21,
        Agility = 23,
        Intellect = 20,
    };

    private static readonly ClassLevelStat[] s_rows = [s_warriorLevel1, s_wizardLevel1, s_hunterLevel1];

    [Fact]
    public void Refill_both_pools_and_mark_the_stats_for_the_next_save()
    {
        CharacterEntity character = New();
        character.ConsumeDirtyFields();

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, EquipTemplates.Find,
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula));

        Assert.Equal(240u, character.Health);
        Assert.Equal(240u, character.CurrentHealth);
        Assert.Equal(100u, character.Power);
        Assert.Equal(100u, character.CurrentPower);
        Assert.Equal(22u, character.Stamina);
        Assert.Equal(240, character.Data!.Health);
        Assert.Equal(100, character.Data.Power1);
        Assert.True(character.SaveState.StatsDirty);
        Assert.Equal(240u, character.Stats!.Value.MaxHealth);

        GameEntityFields dirty = character.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.Health));
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.Power));
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentPower));
    }

    [Fact]
    public void Keep_the_share_of_health_when_gear_goes_on()
    {
        CharacterEntity character = New();
        CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, EquipTemplates.Find,
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula);
        character.CurrentHealth = 120;
        character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, EquipTemplates.Chestguard)]);

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, EquipTemplates.Find,
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.KeepShare, TestCombat.Formula));

        Assert.Equal(250u, character.Health);
        Assert.Equal(125u, character.CurrentHealth);
        Assert.Equal(100u, character.CurrentPower);
    }

    /// <summary>Review Focus 5.</summary>
    [Fact]
    public void Count_only_what_is_worn_in_a_slot_it_fits()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Equipment).Load(
        [
            Item(EquipmentSlots.Chest, EquipTemplates.Chestguard),   // fits: counts
            Item(EquipmentSlots.Finger2, EquipTemplates.Ruby),       // a gem in slot 8: does not
            Item(EquipmentSlots.FirstReserved, EquipTemplates.Longsword),   // a reserved slot: does not
            Item(EquipmentSlots.Head, EquipTemplates.Ghost),         // template gone: does not
        ]);
        character.Container(InventoryType.Bag).Load([Item(0, EquipTemplates.Greatsword)]);

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, EquipTemplates.Find,
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula));

        Assert.Equal(25u, character.Stats!.Value.Strength);
        Assert.Equal(24u, character.Stats.Value.Stamina);
        Assert.Equal(8u + 2u, character.Stats.Value.Armor);   // and Strength 25 x 0.1 (#1178)
    }

    /// <summary>
    /// Review Focus 3 (#1179, B2-R1): a base's defence and weapon damage are the base stats its copy rolled when it was
    /// made, never its template's current ranges. A plate helm that rolled 6 armour gives 6 and an Ironwood Sword that
    /// rolled 3-7 at 700 ms rolls 3-7 at 700; after a reload whose sword ranges 20-30 at its high end and whose helm 40-50,
    /// the next refresh still reads 6 and 3-7 at 700.
    /// </summary>
    [Fact]
    public void Read_base_stats_from_the_copy_and_ignore_a_range_edit()
    {
        ItemTemplate helmBase = ItemBaseSeed.Equipment().Single(t => t.Id.Value == 36);
        ItemTemplate sword = ItemBaseSeed.Equipment().Single(t => t.Id.Value == 32);
        ItemTemplate editedHelm = ItemBaseSeed.Equipment().Single(t => t.Id.Value == 36);
        ItemTemplate editedSword = ItemBaseSeed.Equipment().Single(t => t.Id.Value == 32);
        editedHelm.BaseStats = [new ItemTemplateBaseStat { TemplateId = helmBase.Id, Stat = ItemBaseStat.Armour, Min = 40, Max = 50 }];
        editedSword.BaseStats =
        [
            new ItemTemplateBaseStat { TemplateId = sword.Id, Stat = ItemBaseStat.DamageMin, Min = 2, Max = 4 },
            new ItemTemplateBaseStat { TemplateId = sword.Id, Stat = ItemBaseStat.DamageMax, Min = 20, Max = 30 },
            new ItemTemplateBaseStat { TemplateId = sword.Id, Stat = ItemBaseStat.AttackTime, Min = 665, Max = 735 },
        ];

        CharacterEntity bare = New();
        CharacterStatsRefresh.Apply(bare, s_rows, TestCombat.Factors, EquipTemplates.Find,
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula);
        CharacterEntity character = New();
        InventoryItem helm = Item(EquipmentSlots.Head, helmBase);
        InventoryItem blade = Item(EquipmentSlots.MainHand, sword);
        character.ItemModifiers.Load(
        [
            new ItemInstance
            {
                Identified = true,
                Id = helm.InstanceId,
                TemplateId = helmBase.Id,
                BaseStats = [new ItemInstanceBaseStat { ItemInstanceId = helm.InstanceId, Stat = ItemBaseStat.Armour, Value = 6 }],
            },
            new ItemInstance
            {
                Identified = true,
                Id = blade.InstanceId,
                TemplateId = sword.Id,
                BaseStats =
                [
                    new ItemInstanceBaseStat { ItemInstanceId = blade.InstanceId, Stat = ItemBaseStat.DamageMin, Value = 3 },
                    new ItemInstanceBaseStat { ItemInstanceId = blade.InstanceId, Stat = ItemBaseStat.DamageMax, Value = 7 },
                    new ItemInstanceBaseStat { ItemInstanceId = blade.InstanceId, Stat = ItemBaseStat.AttackTime, Value = 700 },
                ],
            },
        ]);
        character.Container(InventoryType.Equipment).Load([helm, blade]);

        (uint Armor, uint Min, uint Max, uint Time) Refresh(ItemTemplate helmNow, ItemTemplate swordNow)
        {
            CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors,
                id => id == helmNow.Id ? helmNow : id == swordNow.Id ? swordNow : null,
                    ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula);
            DerivedCharacterStats stats = character.Stats!.Value;
            return (stats.Armor, stats.Weapon.Min1, stats.Weapon.Max1, stats.Weapon.AttackTimeMs);
        }

        (uint Armor, uint Min, uint Max, uint Time) before = Refresh(helmBase, sword);
        (uint Armor, uint Min, uint Max, uint Time) after = Refresh(editedHelm, editedSword);

        uint none = bare.Stats!.Value.Armor;
        Assert.Equal((none + 6u, 3u, 7u, 700u), before);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// A unique weapon deals its named base's damage type (#1179), as it rolls its base's ranges: it carries none of its
    /// own, so a unique sword made on a sword typed Fire hits for Fire.
    /// </summary>
    [Fact]
    public void Type_a_unique_weapons_damage_by_its_base()
    {
        ItemTemplate fireSword = ItemBaseSeed.Equipment().Single(t => t.Id.Value == 32);
        fireSword.DamageType = Avalon.Network.Packets.Combat.DamageType.Fire;
        var unique = new ItemTemplate
        {
            Id = 1179_902,
            Name = "Unique Sword",
            Rarity = ItemRarity.Unique,
            BaseKind = ItemBaseKind.Sword,
            BaseTemplateId = fireSword.Id,
            Slot = fireSword.Slot,
            RequiredLevel = fireSword.RequiredLevel,
        };
        CharacterEntity character = New();
        InventoryItem blade = Item(EquipmentSlots.MainHand, unique);
        character.ItemModifiers.Load(
        [
            new ItemInstance
            {
                Identified = true,
                Id = blade.InstanceId,
                TemplateId = unique.Id,
                BaseStats =
                [
                    new ItemInstanceBaseStat { ItemInstanceId = blade.InstanceId, Stat = ItemBaseStat.DamageMin, Value = 3 },
                    new ItemInstanceBaseStat { ItemInstanceId = blade.InstanceId, Stat = ItemBaseStat.DamageMax, Value = 7 },
                    new ItemInstanceBaseStat { ItemInstanceId = blade.InstanceId, Stat = ItemBaseStat.AttackTime, Value = 700 },
                ],
            },
        ]);
        character.Container(InventoryType.Equipment).Load([blade]);

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors,
            id => id == unique.Id ? unique : id == fireSword.Id ? fireSword : null,
                ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula));

        Assert.Equal(Avalon.Network.Packets.Combat.DamageType.Fire, character.Stats!.Value.Weapon.Type1);
    }

    private static readonly Dictionary<ItemTemplateId, ItemTemplate> s_equipment =
        ItemBaseSeed.Equipment().ToDictionary(t => t.Id);

    /// <summary>
    /// Review Focus 1 (#1180): each worn item adds 1 to each theme its prefixes and suffixes carry, however many of its
    /// affixes share it, and nothing else counts: not an implicit, not a unique's own line, not an item in the bag, not one
    /// worn in a slot it does not fit. <paramref name="expected" /> lists the themes above 0, by slot.
    /// </summary>
    [Theory]
    [InlineData("nothing", "")]
    [InlineData("a ring with two Ember affixes", "Ember 1")]
    [InlineData("two Ember items", "Ember 2")]
    [InlineData("both rings", "Ember 2")]
    [InlineData("one item of two themes", "Ember 1, Thorn 1")]
    [InlineData("a helm with its two Ember affixes apart, then an Ember ring", "Ember 2, Thorn 1")]
    [InlineData("a helm with a sealed Ember affix before an awake one, and a ring whose only Ember affix is sealed", "Ember 1")]
    [InlineData("an Ember affix in the bag", "")]
    [InlineData("an item in a slot it does not fit", "")]
    [InlineData("implicits and a unique's lines", "")]
    public async Task Count_each_theme_once_per_worn_item_from_its_affixes_only(string gear, string expected)
    {
        ResonanceTable seeded = await ItemModifierRepositories.SeededResonance();
        int[] emberRing = [CatalogKeys.ModifierId("P8 Fire T9"), CatalogKeys.ModifierId("P10 Fire T9")];
        int heartwoodBase = ItemModifierSeed.TemplateLinks().Single(link => link.TemplateId.Value == 112).ModifierId;
        (InventoryType Container, byte Slot, ulong Template, int[] Modifiers, int[] Sealed)[] items = gear switch
        {
            "nothing" => [],
            "a ring with two Ember affixes" => [(InventoryType.Equipment, EquipmentSlots.Finger1, 68, emberRing, [])],
            "two Ember items" =>
            [
                (InventoryType.Equipment, EquipmentSlots.Finger1, 68, emberRing, []),
                (InventoryType.Equipment, EquipmentSlots.Neck, 69, [CatalogKeys.ModifierId("S5 Fire T9")], []),
            ],
            "both rings" =>
            [
                (InventoryType.Equipment, EquipmentSlots.Finger1, 68, emberRing, []),
                (InventoryType.Equipment, EquipmentSlots.Finger2, 68, emberRing, []),
            ],
            "one item of two themes" =>
            [
                (InventoryType.Equipment, EquipmentSlots.Head, 36,
                    [CatalogKeys.ModifierId("S5 Fire T9"), CatalogKeys.ModifierId("S10 T9")], []),
            ],
            // The helm counts Ember once though a Thorn affix sits between its two, and the ring after it counts Ember
            // again.
            "a helm with its two Ember affixes apart, then an Ember ring" =>
            [
                (InventoryType.Equipment, EquipmentSlots.Head, 36,
                    [CatalogKeys.ModifierId("P8 Fire T9"), CatalogKeys.ModifierId("S10 T9"), CatalogKeys.ModifierId("S5 Fire T9")], []),
                (InventoryType.Equipment, EquipmentSlots.Finger1, 68, emberRing, []),
            ],
            // #1180 part B: a sealed affix counts nothing. The helm counts Ember once, from its awake affix, which its
            // sealed one before it never hides; the ring, whose one Ember affix is sealed, counts nothing.
            "a helm with a sealed Ember affix before an awake one, and a ring whose only Ember affix is sealed" =>
            [
                (InventoryType.Equipment, EquipmentSlots.Head, 36,
                    [CatalogKeys.ModifierId("P8 Fire T9"), CatalogKeys.ModifierId("S5 Fire T9")], [0]),
                (InventoryType.Equipment, EquipmentSlots.Finger1, 68, [CatalogKeys.ModifierId("P10 Fire T9")], [0]),
            ],
            "an Ember affix in the bag" => [(InventoryType.Bag, 0, 68, emberRing, [])],
            "an item in a slot it does not fit" => [(InventoryType.Equipment, EquipmentSlots.Head, 68, emberRing, [])],
            "implicits and a unique's lines" =>
            [
                (InventoryType.Equipment, EquipmentSlots.Head, 36, [CatalogKeys.ModifierId("implicit FireResistance T9")], []),
                (InventoryType.Equipment, EquipmentSlots.Finger1, 64, [heartwoodBase, CatalogKeys.ModifierId("unique 64")], []),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(gear), gear, null),
        };

        CharacterEntity character = New();
        Hold(character, items);

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, s_equipment.GetValueOrDefault, seeded,
            ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula));

        Assert.Equal(expected, ResonanceNamed(character));
    }

    /// <summary>
    /// #1180 part A2: each refresh activates, for each theme, the effect rows of the highest threshold its count reaches
    /// (A2-R4): none below the lowest, the 4 rows above 4. <paramref name="worn" /> names theme counts, each made of that
    /// many worn items carrying one T9 affix of the theme; <paramref name="expected" /> is the active slots in order
    /// ("{effect id} {trigger}", by theme id and then effect id), then the trigger mask.
    /// </summary>
    [Theory]
    [InlineData("nothing", " | None")]
    [InlineData("Ember 1", " | None")]
    [InlineData("Ember 2", "1 Hit | Hit")]
    [InlineData("Ember 3", "2 Hit | Hit")]
    [InlineData("Ember 5", "3 Hit | Hit")]
    [InlineData("Rime 4", "6 Hit, 7 Crit | Hit, Crit")]
    [InlineData("Ember 4, Thorn 4", "3 Hit, 20 Struck, 21 Crit | Hit, Crit, Struck")]
    [InlineData("Wild 4, Veil 2", "13 Kill, 14 Struck, 26 WardBroken | Kill, Struck, WardBroken")]
    public async Task Activate_each_themes_effects_at_its_highest_threshold(string worn, string expected)
    {
        ResonanceTable seeded = await ItemModifierRepositories.SeededResonance();
        Dictionary<string, int> affixOf = new()
        {
            ["Ember"] = CatalogKeys.ModifierId("P8 Fire T9"),
            ["Rime"] = CatalogKeys.ModifierId("P8 Cold T9"),
            ["Wild"] = CatalogKeys.ModifierId("P11 T9"),
            ["Thorn"] = CatalogKeys.ModifierId("S9 T9"),
            ["Veil"] = CatalogKeys.ModifierId("P16 T9"),
        };
        (string Theme, int Count)[] counts = worn == "nothing"
            ? []
            :
            [
                .. worn.Split(", ")
                    .Select(pair => pair.Split(' '))
                    .Select(pair => (pair[0], int.Parse(pair[1], CultureInfo.InvariantCulture))),
            ];

        // The i-th item carries one affix of each theme counted above i, so a theme at n is on n items.
        (byte Slot, ulong Template)[] plate =
        [
            (EquipmentSlots.Head, 36), (EquipmentSlots.Chest, 37), (EquipmentSlots.Legs, 38), (EquipmentSlots.Hands, 39),
            (EquipmentSlots.Feet, 40),
        ];
        (InventoryType, byte, ulong, int[], int[])[] items =
        [
            .. plate
                .Select((piece, i) => (Container: InventoryType.Equipment, piece.Slot, piece.Template,
                    Modifiers: counts.Where(c => c.Count > i).Select(c => affixOf[c.Theme]).ToArray(), Sealed: (int[])[]))
                .Where(item => item.Modifiers.Length > 0),
        ];
        CharacterEntity character = New();
        Hold(character, items);

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, s_equipment.GetValueOrDefault, seeded,
            ItemModifierRepositories.SeededEffects(), CurrentValues.Refill, TestCombat.Formula));

        ResonanceProcs procs = ResonanceProcsOf(character);
        IEnumerable<string> slots =
            Enumerable.Range(0, procs.Count).Select(i => $"{procs.EffectAt(i).Id} {procs.EffectAt(i).Trigger}");
        Assert.Equal(expected, $"{string.Join(", ", slots)} | {procs.Mask}");
    }

    /// <summary>
    /// Puts <paramref name="items" /> in the character's containers, each copy with the modifiers named, each with its
    /// seeded first line's low end as its one value, and those at its <c>Sealed</c> ordinals sealed at progress 0 (#1180).
    /// </summary>
    private static void Hold(CharacterEntity character,
        IEnumerable<(InventoryType Container, byte Slot, ulong Template, int[] Modifiers, int[] Sealed)> items)
    {
        ILookup<int, ItemModifierStat> lines = ItemModifierSeed.Stats().ToLookup(line => line.ModifierId);
        var held = items
            .Select(i => (i.Container, Item: Item(i.Slot, s_equipment[new ItemTemplateId(i.Template)]), i.Modifiers, i.Sealed))
            .ToList();
        character.ItemModifiers.Load(
        [
            .. held.Select(h => new ItemInstance
            {
                Identified = true,
                Id = h.Item.InstanceId,
                TemplateId = h.Item.TemplateId,
                Modifiers =
                [
                    .. h.Modifiers.Select((id, ordinal) =>
                    {
                        ItemModifierStat line = lines[id].OrderBy(l => l.Index).First();
                        ItemInstanceModifier modifier =
                            TestModifiers.Modifier(h.Item.InstanceId, id, (byte)ordinal, (0, line.Stat, line.Min, line.Min2));
                        modifier.SealProgress = h.Sealed.Contains(ordinal) ? 0 : null;
                        return modifier;
                    }),
                ],
            }),
        ]);
        foreach (InventoryType container in (InventoryType[])[InventoryType.Equipment, InventoryType.Bag])
            character.Container(container).Load([.. held.Where(h => h.Container == container).Select(h => h.Item)]);
    }

    /// <summary>
    /// #1180: a refresh over the same worn gear and table (an aura's, a level-up's) keeps the Resonance counts without
    /// walking the items again, and anything that moves the count makes the next refresh count. To tell the two apart,
    /// the ring's one Ember affix becomes a Thorn affix in its live list after the first count, which nothing else may do
    /// (a held item's list never changes in place): a refresh that counts again reads Thorn or nothing, one that keeps
    /// the counts still reads Ember.
    /// </summary>
    [Theory]
    [InlineData("a refresh that moves no gear", "Ember 1")]
    [InlineData("the ring taken off", "")]
    [InlineData("a template edit the ring's slot no longer fits", "")]
    [InlineData("a reload of the catalog", "Thorn 1")]
    public async Task Count_Resonance_again_only_when_the_worn_gear_or_the_table_moved(string change, string expected)
    {
        ResonanceTable seeded = await ItemModifierRepositories.SeededResonance();
        ItemTemplate ringBase = s_equipment[new ItemTemplateId(68)];
        InventoryItem ring = Item(EquipmentSlots.Finger1, ringBase);
        CharacterEntity character = New();
        character.ItemModifiers.Load(
        [
            new ItemInstance
            {
                Identified = true,
                Id = ring.InstanceId,
                TemplateId = ring.TemplateId,
                Modifiers =
                [
                    TestModifiers.Modifier(ring.InstanceId, CatalogKeys.ModifierId("P8 Fire T9"), 0,
                        (0, StatType.FireResistance, 1, null)),
                ],
            },
        ]);
        character.Container(InventoryType.Equipment).Load([ring]);
        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, s_equipment.GetValueOrDefault, seeded,
            ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula));
        Assert.Equal("Ember 1", ResonanceNamed(character));

        character.ItemModifiers.Of(ring.InstanceId)[0].ModifierId = CatalogKeys.ModifierId("S10 T9");
        Func<ItemTemplateId, ItemTemplate?> find = s_equipment.GetValueOrDefault;
        ResonanceTable table = seeded;
        switch (change)
        {
            case "a refresh that moves no gear":
                break;
            case "the ring taken off":
                character.Container(InventoryType.Equipment).Remove(EquipmentSlots.Finger1);
                break;
            case "a template edit the ring's slot no longer fits":
                ItemTemplate edited = new() { Id = ringBase.Id, Name = ringBase.Name, Slot = ItemSlotType.Head, MaxStackSize = 1 };
                find = id => id == ringBase.Id ? edited : s_equipment.GetValueOrDefault(id);
                break;
            case "a reload of the catalog":
                table = await ItemModifierRepositories.SeededResonance();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, find, table, ResonanceEffectCatalog.Empty,
            CurrentValues.KeepShare, TestCombat.Formula));
        Assert.Equal(expected, ResonanceNamed(character));
    }

    /// <summary>The character's Resonance counts above 0, by slot, as "Theme count" by the seeded theme names.</summary>
    private static string ResonanceNamed(CharacterEntity character)
    {
        Dictionary<int, string> names = ItemModifierSeed.Themes().ToDictionary(theme => theme.Id, theme => theme.Name);
        List<string> counted = [];
        foreach (ThemeCount pair in ResonanceOf(character))
            counted.Add($"{names[pair.ThemeId]} {pair.Count}");
        return string.Join(", ", counted);
    }

    [Fact]
    public void Change_nothing_when_the_class_and_level_have_no_row()
    {
        CharacterEntity character = New();
        character.Level = 9;
        character.Data!.Health = 150;
        character.CurrentHealth = 150;

        Assert.False(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, EquipTemplates.Find,
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, TestCombat.Formula));

        Assert.Equal(150u, character.Health);
        Assert.Equal(150u, character.CurrentHealth);
        Assert.Null(character.Stats);
        Assert.False(character.SaveState.StatsDirty);
    }

    /// <summary>
    /// #1181: every class's pool is mana, treated as health is: select and a level-up fill it, a gear change keeps its
    /// share (60 of 200 is 30 %), and the regeneration rates follow the new maximum. The Warrior's maximum is its fixed
    /// factor's 100, the Wizard's 20 + 23 x 15 = 365.
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Warrior, CurrentValues.EnterWorld, 100u)]
    [InlineData(CharacterClass.Warrior, CurrentValues.Refill, 100u)]
    [InlineData(CharacterClass.Warrior, CurrentValues.KeepShare, 30u)]
    [InlineData(CharacterClass.Wizard, CurrentValues.EnterWorld, 365u)]
    [InlineData(CharacterClass.Wizard, CurrentValues.Refill, 365u)]
    [InlineData(CharacterClass.Wizard, CurrentValues.KeepShare, 110u)]
    public void Fill_or_keep_the_share_of_mana_for_every_class(CharacterClass @class, CurrentValues current, uint expected)
    {
        CharacterEntity character = New();
        character.Data!.Class = @class;
        character.PowerType = Avalon.Network.Packets.State.PowerType.Mana;
        character.Power = 200;
        character.CurrentPower = 60;

        Assert.True(CharacterStatsRefresh.Apply(character, s_rows, TestCombat.Factors, EquipTemplates.Find,
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, current, TestCombat.Formula));

        Assert.Equal(expected, character.CurrentPower);
        Assert.Equal(ManaRegenRates.Of(new RegenConfiguration(), character.Power!.Value, 0f), ManaRegenOf(character));
    }
}
