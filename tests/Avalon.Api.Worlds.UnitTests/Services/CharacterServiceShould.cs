using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Api.Contract;
using Avalon.Api.Testing;
using Avalon.Api.Worlds.Services;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using AbilityTags = Avalon.Network.Packets.Abilities.AbilityTags;
using ItemBaseStat = Avalon.Domain.World.ItemBaseStat;

namespace Avalon.Api.Worlds.UnitTests.Services;

public sealed class CharacterServiceShould : IDisposable
{
    // The Ironwood Sword (32) as a copy rolled it, within its seeded 2-4, 6-8 and 665-735 ms.
    private static readonly ItemBaseStats s_rolledSword = new() { DamageMin = 3, DamageMax = 7, AttackTime = 700 };

    // The seeded nodes from the Warrior's start to Cleave (#1181): +5 Strength, 5 % increased physical damage, 2 % attack
    // speed, 3 % increased maximum life, then Cleave itself.
    private static readonly int[] s_toCleave = [2, 3, 4, 5, 6];

    // The seeded world, whose modifier catalog (#1179) the service reads as the API does: only the rows the items carry.
    private readonly SqliteWorlds _world = new(1);

    public void Dispose() => _world.Dispose();

    private DelegateDbContextFactory<WorldDbContext> SeededWorld() =>
        new(() => _world.CreateWorld(new Avalon.Domain.Auth.WorldId(1)));

    private IItemModifierRepository Modifiers() => new ItemModifierRepository(SeededWorld(), NullLoggerFactory.Instance);
    /// <summary>A seeded affix's id, named by its family's code, element (null on an unsplit family) and tier: no test holds an id (#1179).</summary>
    private static int Seeded(string code, Avalon.Network.Packets.Combat.DamageType? element, byte tier)
    {
        int group = ItemModifierSeed.Groups().Single(g => g.Code == code && g.Element == element).Id;
        return ItemModifierSeed.Modifiers().Single(m => m.GroupId == group && m.Tier == tier).Id;
    }

    /// <summary>
    /// Instances live in the Character database and templates in the World database, so the
    /// template is looked up by id rather than joined. The copy's own rarity, item level and modifiers come from the
    /// instance (#1179), in the order the item shows them, each with its values in index order, whatever order the rows
    /// were read in, each with its kind from the world's catalog; a magic copy is named by its prefix and its suffix, and
    /// its implicit names nothing. The magic copy is a ring: its implicit (the Copper Band's life), its prefix (Ember-sung,
    /// cold, T8) a two-range line, its suffix (of the Owl, T8) a plain one. The unique copy, a Thornheart Signet, carries
    /// its base's implicit and its own three lines, read back in reverse. The plate chest carries the armour it rolled, a
    /// base stat; the rings carry none. An unidentified rare chest shows its rarity, item level, armour and implicit, and
    /// neither its prefix, its suffix nor its name, to every caller. Each rare chest's prefix is sealed (#1180): the
    /// identified one shows the seal's progress and the threshold for its item level, the unidentified one neither. Only
    /// the attuned ring reads attuned. The identified rare chest bricked (#1180): it reads Tainted and its imprinted suffix
    /// reads imprinted, and its instability, 130, appears nowhere in the read.
    /// </summary>
    [Fact]
    public async Task Look_up_each_items_template_in_the_world_database()
    {
        int emberSung = Seeded("P10", Avalon.Network.Packets.Combat.DamageType.Cold, 8);
        int owl = Seeded("S3", null, 8);
        int life = ImplicitOf(68);
        int lightning = ImplicitOf(125);
        int chestLife = ImplicitOf(37);
        int signet = ItemModifierSeed.TemplateLinks().Single(l => l.TemplateId.Value == 65).ModifierId;
        var id = new CharacterId(42);
        var itemId = new ItemInstanceId(Guid.CreateVersion7());
        var uniqueId = new ItemInstanceId(Guid.CreateVersion7());
        var chestId = new ItemInstanceId(Guid.CreateVersion7());
        var rareChestId = new ItemInstanceId(Guid.CreateVersion7());
        var sealedChestId = new ItemInstanceId(Guid.CreateVersion7());

        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Holder" });

        ICharacterInventoryRepository slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterInventory>
        {
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 3, ItemId = itemId },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 4, ItemId = uniqueId },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 5, ItemId = chestId },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 6, ItemId = rareChestId },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 7, ItemId = sealedChestId },
        });

        static ItemInstanceModifier Rolled(ItemInstanceId item, int modifierId, byte ordinal,
            params (byte Index, Avalon.Domain.World.StatType Stat, int Value, int? Value2)[] values) => new()
            {
                Id = Guid.CreateVersion7(),
                ItemInstanceId = item,
                ModifierId = modifierId,
                Ordinal = ordinal,
                Values =
                [
                    .. values.Select(v => new ItemInstanceModifierValue { Index = v.Index, Stat = v.Stat, Value = v.Value, Value2 = v.Value2 }),
                ],
            };

        static ItemInstanceModifier SealedAt(int progress, ItemInstanceModifier row)
        {
            row.SealProgress = progress;
            return row;
        }

        static ItemInstanceModifier Imprinted(ItemInstanceModifier row)
        {
            row.Imprinted = true;
            return row;
        }

        IItemInstanceRepository items = Substitute.For<IItemInstanceRepository>();
        items.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<ItemInstance>
        {
            new()
            {
                Identified = true,
                Id = itemId, TemplateId = new ItemTemplateId(68), CharacterId = id, Count = 4, Durability = 7,
                Rarity = Avalon.Common.ItemRarity.Magic, ItemLevel = 7, Flags = ItemInstanceFlags.Attuned,
                Modifiers =
                [
                    Rolled(itemId, owl, 2, (0, Avalon.Domain.World.StatType.Intellect, 2, null)),
                    Rolled(itemId, emberSung, 1, (0, Avalon.Domain.World.StatType.AddedColdDamageToSpells, 1, 2)),
                    Rolled(itemId, life, 0, (0, Avalon.Domain.World.StatType.Health, 2, null)),
                ],
            },
            new()
            {
                Identified = true,
                Id = uniqueId, TemplateId = new ItemTemplateId(65), CharacterId = id, Count = 1,
                Rarity = Avalon.Common.ItemRarity.Unique, ItemLevel = 5,
                Modifiers =
                [
                    Rolled(uniqueId, signet, 1,
                        (2, Avalon.Domain.World.StatType.CritMultiplier, 4, null),
                        (1, Avalon.Domain.World.StatType.AddedPhysicalDamageToAttacks, 1, 3),
                        (0, Avalon.Domain.World.StatType.Health, 5, null)),
                    Rolled(uniqueId, lightning, 0, (0, Avalon.Domain.World.StatType.AddedLightningDamageToAttacks, 1, 2)),
                ],
            },
            new()
            {
                Identified = true,
                Id = chestId, TemplateId = new ItemTemplateId(37), CharacterId = id, Count = 1,
                Rarity = Avalon.Common.ItemRarity.Common, ItemLevel = 1,
                BaseStats = [new ItemInstanceBaseStat { ItemInstanceId = chestId, Stat = ItemBaseStat.Armour, Value = 8 }],
            },
            new()
            {
                Identified = false,
                Id = rareChestId, TemplateId = new ItemTemplateId(37), CharacterId = id, Count = 1,
                Rarity = Avalon.Common.ItemRarity.Rare, ItemLevel = 9, RareName = "Moss Shell",
                BaseStats = [new ItemInstanceBaseStat { ItemInstanceId = rareChestId, Stat = ItemBaseStat.Armour, Value = 8 }],
                Modifiers =
                [
                    Rolled(rareChestId, Seeded("S14", null, 9), 2, (0, Avalon.Domain.World.StatType.IncreasedMaximumLife, 1, null)),
                    Rolled(rareChestId, chestLife, 0, (0, Avalon.Domain.World.StatType.IncreasedMaximumLife, 2, null)),
                    SealedAt(5, Rolled(rareChestId, Seeded("P11", null, 7), 1, (0, Avalon.Domain.World.StatType.Health, 5, null))),
                ],
            },
            new()
            {
                Identified = true,
                Id = sealedChestId, TemplateId = new ItemTemplateId(37), CharacterId = id, Count = 1,
                Rarity = Avalon.Common.ItemRarity.Rare, ItemLevel = 12, RareName = "Grave Hide",
                Tainted = true, Instability = 130,
                Modifiers =
                [
                    SealedAt(5, Rolled(sealedChestId, Seeded("P11", null, 7), 1, (0, Avalon.Domain.World.StatType.Health, 5, null))),
                    Rolled(sealedChestId, chestLife, 0, (0, Avalon.Domain.World.StatType.IncreasedMaximumLife, 2, null)),
                    Imprinted(Rolled(sealedChestId, Seeded("S14", null, 9), 2, (0, Avalon.Domain.World.StatType.IncreasedMaximumLife, 1, null))),
                ],
            },
        });

        IItemTemplateRepository templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemTemplate>
            {
                new() { Id = new ItemTemplateId(68), Name = "Copper Band", Rarity = Avalon.Common.ItemRarity.Common, DisplayId = 68 },
                new() { Id = new ItemTemplateId(65), Name = "Thornheart Signet", Rarity = Avalon.Common.ItemRarity.Unique, DisplayId = 65 },
                new() { Id = new ItemTemplateId(37), Name = "Ironbound Chestguard", Rarity = Avalon.Common.ItemRarity.Common, DisplayId = 37 },
            });

        var service = new CharacterService(characters, slots, items,
            Substitute.For<ICharacterTalentRepository>(), Substitute.For<IAbilityTemplateRepository>(), templates,
            Modifiers(), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>(), Substitute.For<ICombatDataRepository>(),
            Substitute.For<ITalentTreeRepository>(), Substitute.For<IQuestRepository>());

        CharacterInventoryDto? inventory = await service.GetInventoryAsync(id);

        static string Described(CharacterInventoryItemDto item) => string.Join(' ', item.Modifiers.Select(m =>
            $"{m.ModifierId} {m.Kind} [{string.Join(", ", m.Values.Select(v => $"{v.Stat} {v.Value} {v.Value2}"))}]"));

        Assert.Equal(5, inventory!.Items.Count);
        CharacterInventoryItemDto item = Assert.Single(inventory.Items, i => i.ItemId == itemId.Value);
        Assert.Equal(4u, item.Count);
        Assert.Equal(7u, item.Durability);
        Assert.NotNull(item.Template);
        Assert.Equal(68ul, item.Template!.Id);
        Assert.Equal("Copper Band", item.Template.Name);
        Assert.Equal(68u, item.Template.DisplayId);
        Assert.Equal((Avalon.Api.Contract.ItemRarity.Magic, (ushort)7), (item.Rarity, item.ItemLevel));
        Assert.True(item.Identified);
        Assert.Equal("Ember-sung Copper Band of the Owl", item.Name);
        Assert.Equal(
            $"{life} Implicit [Health 2 ] {emberSung} Prefix [AddedColdDamageToSpells 1 2] {owl} Suffix [Intellect 2 ]",
            Described(item));

        CharacterInventoryItemDto unique = Assert.Single(inventory.Items, i => i.ItemId == uniqueId.Value);
        Assert.Null(unique.Name);
        Assert.Equal(
            $"{lightning} Implicit [AddedLightningDamageToAttacks 1 2] " +
            $"{signet} Unique [Health 5 , AddedPhysicalDamageToAttacks 1 3, CritMultiplier 4 ]",
            Described(unique));

        CharacterInventoryItemDto chest = Assert.Single(inventory.Items, i => i.ItemId == chestId.Value);
        Assert.Equal([(Avalon.Api.Contract.ItemBaseStat.Armour, 8)], chest.BaseStats.Select(s => (s.Stat, s.Value)));
        Assert.Empty(item.BaseStats);

        CharacterInventoryItemDto rareChest = Assert.Single(inventory.Items, i => i.ItemId == rareChestId.Value);
        Assert.Equal((false, null, Avalon.Api.Contract.ItemRarity.Rare, (ushort)9),
            (rareChest.Identified, rareChest.Name, rareChest.Rarity, rareChest.ItemLevel));
        Assert.Equal([(Avalon.Api.Contract.ItemBaseStat.Armour, 8)], rareChest.BaseStats.Select(s => (s.Stat, s.Value)));
        Assert.Equal($"{chestLife} Implicit [IncreasedMaximumLife 2 ]", Described(rareChest));

        // #1180: each affix line names its Resonance theme (Ember-sung, a cold P10, is Rime; of the Owl is Veil), an
        // implicit or unique line none, and the unidentified chest's hidden affixes show no theme either.
        Assert.Equal([(life, (int?)null), (emberSung, ThemeId("Rime")), (owl, ThemeId("Veil"))],
            item.Modifiers.Select(m => (m.ModifierId, m.ThemeId)));
        Assert.All(unique.Modifiers, m => Assert.Null(m.ThemeId));
        Assert.Equal([(chestLife, (int?)null)], rareChest.Modifiers.Select(m => (m.ModifierId, m.ThemeId)));

        // #1180: the identified rare chest's sealed prefix shows its 5 points and the 160 it wakes at (item level 12:
        // 40 + 120), its implicit no seal; the unidentified chest hides its sealed prefix with its numbers.
        CharacterInventoryItemDto sealedChest = Assert.Single(inventory.Items, i => i.ItemId == sealedChestId.Value);
        Assert.Equal([(chestLife, (int?)null, (int?)null), (Seeded("P11", null, 7), 5, 160), (Seeded("S14", null, 9), null, null)],
            sealedChest.Modifiers.Select(m => (m.ModifierId, m.SealProgress, m.SealThreshold)));
        Assert.All(rareChest.Modifiers, m => Assert.Equal(((int?)null, (int?)null), (m.SealProgress, m.SealThreshold)));
        Assert.Equal([itemId.Value], inventory.Items.Where(i => i.Attuned).Select(i => i.ItemId));

        // #1180: only the bricked chest reads Tainted, and only its imprinted suffix reads imprinted.
        Assert.Equal([sealedChestId.Value], inventory.Items.Where(i => i.Tainted).Select(i => i.ItemId));
        Assert.Equal([(sealedChestId.Value, Seeded("S14", null, 9))], inventory.Items
            .SelectMany(i => i.Modifiers.Where(m => m.Imprinted).Select(m => (i.ItemId, m.ModifierId))));

        // Its instability is never sent (#1180): not as a member, not as its number, under the API's JSON options. A
        // Guid's groups are 4 hex digits or more, so "130" standing alone is no part of an item's id.
        string json = JsonSerializer.Serialize(inventory, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        Assert.DoesNotContain("instab", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"\b130\b", json);
    }

    /// <summary>A seeded Resonance theme's id, by its name (#1180): no test holds an id.</summary>
    private static int? ThemeId(string name) => ItemModifierSeed.Themes().Single(theme => theme.Name == name).Id;

    /// <summary>A seeded base's implicit row, named by the base (#1179): no test holds an id.</summary>
    private static int ImplicitOf(ulong template) =>
        ItemModifierSeed.TemplateLinks().Single(l => l.TemplateId.Value == template).ModifierId;

    /// <summary>
    /// A template with no slot (a potion, a scroll) has no slot type; it used to come back as Head,
    /// the enum's first value (#674).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(Avalon.Domain.World.ItemSlotType.MainHand)]
    public async Task Return_the_templates_slot_type_or_null_when_it_has_none(Avalon.Domain.World.ItemSlotType? slot)
    {
        var id = new CharacterId(42);
        var itemId = new ItemInstanceId(Guid.CreateVersion7());

        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Holder" });

        ICharacterInventoryRepository slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterInventory>
        {
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 0, ItemId = itemId },
        });

        IItemInstanceRepository items = Substitute.For<IItemInstanceRepository>();
        items.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<ItemInstance>
        {
            new() { Id = itemId, TemplateId = new ItemTemplateId(1), CharacterId = id, Count = 40, Identified = true },
        });

        IItemTemplateRepository templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemTemplate> { new() { Id = new ItemTemplateId(1), Name = "Health Potion", Slot = slot } });

        var service = new CharacterService(characters, slots, items,
            Substitute.For<ICharacterTalentRepository>(), Substitute.For<IAbilityTemplateRepository>(), templates,
            Modifiers(), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>(), Substitute.For<ICombatDataRepository>(),
            Substitute.For<ITalentTreeRepository>(), Substitute.For<IQuestRepository>());

        CharacterInventoryDto? inventory = await service.GetInventoryAsync(id);

        CharacterInventoryItemDto item = Assert.Single(inventory!.Items);
        Assert.Equal((Avalon.Api.Contract.ItemSlotType?)slot, item.Template!.SlotType);
    }

    /// <summary>The seeded talent tree, as the world database holds it.</summary>
    private static ITalentTreeRepository SeededTree()
    {
        ITalentTreeRepository tree = Substitute.For<ITalentTreeRepository>();
        tree.GetTreeAsync(Arg.Any<CancellationToken>()).Returns(TalentSeed.Rows());
        return tree;
    }

    /// <summary>The saved talent rows of a character holding <paramref name="nodeIds" />, by ascending node id.</summary>
    private static ICharacterTalentRepository Holding(CharacterId id, params int[] nodeIds)
    {
        ICharacterTalentRepository talents = Substitute.For<ICharacterTalentRepository>();
        talents.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>())
            .Returns([.. nodeIds.Order().Select(node => new CharacterTalent { CharacterId = id, NodeId = node })]);
        return talents;
    }

    /// <summary>
    /// A Warrior at <paramref name="level" /> holding <paramref name="nodeIds" /> in the seeded tree, whose ability
    /// templates read back as <paramref name="ability" /> alone.
    /// </summary>
    private static CharacterService AbilityService(CharacterId id, AbilityTemplate ability, CharacterStats? stats,
        IReadOnlyList<ItemInstance> worn, IReadOnlyList<ItemTemplate> templateRows, CombatFormula formula,
        ushort level, int[] nodeIds)
    {
        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character
            {
                Id = id,
                Name = "Caster",
                Class = CharacterClass.Warrior,
                Level = level,
            });

        IAbilityTemplateRepository abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.GetByIdsAsync(Arg.Any<IEnumerable<AbilityId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AbilityTemplate> { ability });

        // Each item in the slot its template fits; a second ring goes on the second finger.
        int rings = 0;
        byte SlotOf(ItemInstance item)
        {
            Avalon.Domain.World.ItemSlotType? type = templateRows.First(t => t.Id == item.TemplateId).Slot;
            return type == Avalon.Domain.World.ItemSlotType.Finger
                ? (rings++ == 0 ? EquipmentSlots.Finger1 : EquipmentSlots.Finger2)
                : Enumerable.Range(0, EquipmentSlots.FirstReserved).Select(slot => (byte)slot)
                    .First(slot => EquipmentSlots.Accepts(slot, type));
        }
        ICharacterInventoryRepository slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(worn
            .Select(item => new CharacterInventory
            {
                CharacterId = id,
                Container = Avalon.Common.InventoryType.Equipment,
                Slot = SlotOf(item),
                ItemId = item.Id,
            })
            .ToList());
        IItemInstanceRepository instances = Substitute.For<IItemInstanceRepository>();
        instances.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(worn.ToList());
        // As the repository does: one template per id, however many items share it.
        IItemTemplateRepository templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(templateRows.DistinctBy(t => t.Id).ToList());

        ICharacterStatsRepository statsRepository = Substitute.For<ICharacterStatsRepository>();
        statsRepository.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(stats);
        ICombatDataRepository combat = Substitute.For<ICombatDataRepository>();
        combat.GetFormulasAsync(Arg.Any<CancellationToken>()).Returns(new List<CombatFormula> { formula });

        return new CharacterService(characters, slots, instances, Holding(id, nodeIds), abilities, templates,
            Substitute.For<IItemModifierRepository>(), statsRepository,
            Substitute.For<ICharacterQuestRepository>(), Substitute.For<ICharacterAuraRepository>(), combat,
            SeededTree(), Substitute.For<IQuestRepository>());
    }

    /// <summary>
    /// A saved copy of <paramref name="template" /> with no base stats and one modifier per value, as the Character
    /// database loads it.
    /// </summary>
    private static ItemInstance Saved(ItemTemplate template,
        params (Avalon.Domain.World.StatType Stat, int Value, int? Value2)[] values) => Saved(template, default, values);

    private static ItemInstance Saved(ItemTemplate template, ItemBaseStats baseStats,
        params (Avalon.Domain.World.StatType Stat, int Value, int? Value2)[] values) =>
        Saved(template, baseStats, sealedOrdinal: null, values);

    /// <summary>
    /// A saved copy of <paramref name="template" /> as the Character database loads it: a row per base stat it rolled,
    /// and one modifier per value with its value, the one at <paramref name="sealedOrdinal" /> sealed (#1180).
    /// </summary>
    private static ItemInstance Saved(ItemTemplate template, ItemBaseStats baseStats, int? sealedOrdinal,
        (Avalon.Domain.World.StatType Stat, int Value, int? Value2)[] values)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        List<ItemInstanceBaseStat> rows = [];
        foreach ((ItemBaseStat stat, int value) in baseStats)
            rows.Add(new ItemInstanceBaseStat { ItemInstanceId = id, Stat = stat, Value = value });

        return new ItemInstance
        {
            Identified = true,
            Id = id,
            TemplateId = template.Id,
            Count = 1,
            BaseStats = rows,
            Modifiers = [.. values.Select((v, ordinal) =>
            {
                var modifierId = Guid.CreateVersion7();
                return new ItemInstanceModifier
                {
                    Id = modifierId,
                    ItemInstanceId = id,
                    ModifierId = 1,
                    Ordinal = (byte)ordinal,
                    SealProgress = ordinal == sealedOrdinal ? 0 : null,
                    Values =
                    [
                        new ItemInstanceModifierValue
                        {
                            ItemInstanceModifierId = modifierId, Index = 0, Stat = v.Stat, Value = v.Value, Value2 = v.Value2,
                        },
                    ],
                };
            })],
        };
    }

    /// <summary>
    /// #1178, #1179 (Review Focus 5, B2 Review Focus 3): the worlds service advertises exactly what the world server sends
    /// for the same saved character: AbilityAmounts over the stats calculator's attacker for the same row and worn items,
    /// as the world builds it at select, the sword's damage read from its saved base-stat rows. Every row holds the
    /// seeded nodes to Cleave (#1181), which the world folds from the tree and the API from the saved talent rows: +5
    /// Strength and 5 % increased physical damage count here (attack speed moves no amount). A saved level-1 Warrior
    /// (Strength 23 + 5: #1179's bases carry no attributes) in the starter armour and an Ironwood Sword that rolled 3-7 at
    /// 700 ms: Cleave rolls 3..7 at 315 % effectiveness and 98 + 5 % increased physical attack damage, 19..44. With the
    /// same ring on both fingers (10 % increased physical damage each), both count, as in the world: 123 %, 21..49. With
    /// the sword's own added fire 2..5 and a Copper Band's added physical damage to attacks 1..3, each read from its saved
    /// instance's modifiers: 31..79. With the sword's added fire sealed (#1180), it adds nothing, on either side: the
    /// starter gear's 19..44. At level 7 (Strength 35 + 5) in the starter gear: 145 %, 23..54.
    /// </summary>
    [Theory]
    [InlineData("starter gear", 1, 19u, 44u)]
    [InlineData("two rings", 1, 21u, 49u)]
    [InlineData("a modified sword and a global ring", 1, 31u, 79u)]
    [InlineData("a sword whose added fire is sealed", 1, 19u, 44u)]
    [InlineData("starter gear", 7, 23u, 54u)]
    public async Task Advertise_the_amounts_the_world_sends_for_the_same_saved_character(string gear, ushort level,
        uint min, uint max)
    {
        var id = new CharacterId(42);
        using var sqlite = new SqliteWorlds(1);
        using WorldDbContext world = sqlite.CreateWorld(new WorldId(1));
        ClassLevelStat row = world.ClassLevelStats.AsEnumerable()
            .Single(r => r.Class == CharacterClass.Warrior && r.Level == level);
        CombatFormula formula = world.CombatFormulas.Single();
        var templates = world.ItemTemplates.AsEnumerable().ToDictionary(t => t.Id.Value);
        List<ItemInstance> worn = [.. new ulong[] { 36, 37, 38, 39, 40 }.Select(t => Saved(templates[t]))];
        switch (gear)
        {
            case "starter gear":
                worn.Add(Saved(templates[32], s_rolledSword));
                break;
            case "two rings":
                var ring = new ItemTemplate
                {
                    Id = new ItemTemplateId(990),
                    Name = "Band of Edges",
                    Slot = Avalon.Domain.World.ItemSlotType.Finger,
                    StatType1 = Avalon.Domain.World.StatType.IncreasedPhysicalDamage,
                    StatValue1 = 10,
                };
                templates[990] = ring;
                worn.AddRange([Saved(templates[32], s_rolledSword), Saved(ring), Saved(ring)]);
                break;
            case "a modified sword and a global ring":
                worn.Add(Saved(templates[32], s_rolledSword, (Avalon.Domain.World.StatType.AddedFireDamage, 2, 5)));
                worn.Add(Saved(templates[68], (Avalon.Domain.World.StatType.AddedPhysicalDamageToAttacks, 1, 3)));
                break;
            case "a sword whose added fire is sealed":
                worn.Add(Saved(templates[32], s_rolledSword, sealedOrdinal: 0, [(Avalon.Domain.World.StatType.AddedFireDamage, 2, 5)]));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gear), gear, null);
        }

        List<ItemTemplate> wornTemplates = [.. worn.Select(item => templates[item.TemplateId.Value])];
        AbilityTemplate cleave = world.AbilityTemplates.AsEnumerable().Single(a => a.Id.Value == 200);
        // The world's side: the same five nodes' lines summed, folded by the stats refresh.
        var talents = new TalentTotals();
        foreach (TalentNodeStat line in TalentSeed.Stats().Where(s => s_toCleave.Contains(s.NodeId)))
            talents.Add(line.Stat, line.Kind, line.Value, line.ValueMax);
        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row,
            [.. worn.Select((item, i) => new WornItem(wornTemplates[i], ItemBaseStats.Of(item.BaseStats), item.Modifiers,
                wornTemplates[i].DamageType))],
            formula, talents: talents);
        AbilityAmount sent = AbilityAmounts.For(stats.AttackerAt(level), AbilityMetadataMapper.From(cleave), default);

        // The API's side reads the saved rows.
        CharacterAbilitiesDto? result = await AbilityService(id, cleave, stats.ToRow(id), worn, wornTemplates, formula,
            level, s_toCleave).GetAbilitiesAsync(id);

        // Heavy Strike, the Warrior's starter, then Cleave.
        Assert.Equal([319u, 200u], result!.Abilities.Select(a => a.AbilityId));
        CharacterAbilityAmountDto amount = result.Abilities[1].Amount!;
        Assert.Equal(row.Strength + 5u, stats.Strength);
        Assert.Equal((Avalon.Api.Contract.AbilityAmountKind.Damage, min, max), (amount.Kind, amount.Min, amount.Max));
        Assert.Equal(((Avalon.Api.Contract.AbilityAmountKind)sent.Kind, sent.Min, sent.Max),
            (amount.Kind, amount.Min, amount.Max));
    }

    /// <summary>
    /// No saved stats and no weapon: the bare hand's 3..6 at Cleave's 315 %, with no attributes and only the tree's 5 %
    /// increased physical damage from the nodes to Cleave, 9..19.
    /// </summary>
    [Fact]
    public async Task Compute_amounts_without_stats_or_a_weapon()
    {
        var id = new CharacterId(42);
        AbilityTemplate cleave = new()
        {
            Id = new AbilityId(200),
            Name = "Cleave",
            ScriptName = "ConeAbilityScript",
            Affects = AbilityAffects.Hostile,
            Effects = Avalon.World.Public.Enums.SpellEffect.Damage,
            Tags = AbilityTags.Attack | AbilityTags.Melee,
            DamageType = Avalon.Network.Packets.Combat.DamageType.Physical,
            DamageEffectiveness = 315f,
        };

        CharacterAbilitiesDto? result = await AbilityService(id, cleave, stats: null, worn: [], templateRows: [],
            CombatSeed.Formula(), level: 1, s_toCleave).GetAbilitiesAsync(id);

        CharacterAbilityAmountDto amount = result!.Abilities.Single(a => a.AbilityId == 200).Amount!;
        Assert.Equal((Avalon.Api.Contract.AbilityAmountKind.Damage, 9u, 19u), (amount.Kind, amount.Min, amount.Max));
    }

    /// <summary>
    /// A character's abilities come from the tree (#1181), each with its cost in mana from the world database: a Warrior
    /// holding the hub's first nodes to Hurled Axe (57-61) has Heavy Strike, its starter (cost 0), then Hurled Axe (cost
    /// 10). A saved row naming a node the tree lacks (999) adds nothing, as the world ignores it until its next select.
    /// </summary>
    [Fact]
    public async Task Return_each_abilitys_cost()
    {
        var id = new CharacterId(42);

        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Caster", Class = CharacterClass.Warrior });

        ICombatDataRepository combat = Substitute.For<ICombatDataRepository>();
        combat.GetFormulasAsync(Arg.Any<CancellationToken>()).Returns(new List<CombatFormula> { CombatSeed.Formula() });

        var service = new CharacterService(characters, Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Holding(id, 57, 58, 59, 60, 61, 999),
            new AbilityTemplateRepository(SeededWorld()), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(),
            Substitute.For<ICharacterQuestRepository>(), Substitute.For<ICharacterAuraRepository>(), combat, SeededTree(),
            Substitute.For<IQuestRepository>());

        CharacterAbilitiesDto? result = await service.GetAbilitiesAsync(id);

        Assert.Equal([("Heavy Strike", 0u), ("Hurled Axe", 10u)],
            result!.Abilities.Select(a => (a.Template!.Name, a.Template.Cost)));
    }

    /// <summary>
    /// A character's saved talent nodes with the points it has, worked out as the world works them out (#1181): a
    /// level-7 character holding nodes 2, 3 and 4 that turned in quests 3 (one talent point) and 10 (none) has earned 8
    /// and spent 3. A character that does not exist has none.
    /// </summary>
    [Fact]
    public async Task Return_the_characters_talents_with_the_points_it_earned()
    {
        var id = new CharacterId(42);
        DateTime turnedIn = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Holder", Class = CharacterClass.Warrior, Level = 7 });
        ICharacterQuestRepository quests = Substitute.For<ICharacterQuestRepository>();
        quests.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new CharacterQuestRows([], [],
        [
            new CharacterCompletedQuest { CharacterId = id, QuestId = 3, CompletedAt = turnedIn, LastCompletedAt = turnedIn, CompletionCount = 1 },
            new CharacterCompletedQuest { CharacterId = id, QuestId = 10, CompletedAt = turnedIn, LastCompletedAt = turnedIn, CompletionCount = 1 },
        ]));

        var service = new CharacterService(characters, Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Holding(id, 2, 3, 4), Substitute.For<IAbilityTemplateRepository>(),
            Substitute.For<IItemTemplateRepository>(), Substitute.For<IItemModifierRepository>(),
            Substitute.For<ICharacterStatsRepository>(), quests, Substitute.For<ICharacterAuraRepository>(),
            Substitute.For<ICombatDataRepository>(), SeededTree(), new QuestRepository(SeededWorld()));

        CharacterTalentsDto? talents = await service.GetTalentsAsync(id);

        Assert.NotNull(talents);
        Assert.Equal((42u, 8, 3), (talents!.CharacterId, talents.PointsEarned, talents.PointsSpent));
        Assert.Equal([2, 3, 4], talents.NodeIds);
        Assert.Null(await service.GetTalentsAsync(new CharacterId(43)));
    }

    /// <summary>
    /// The stats the world server last saved for the character (#676), with the Resonance its worn items carry (#1180),
    /// counted from the saved inventory as the world's stats refresh counts it: a ring with two Ember affixes adds Ember
    /// once, an Ember amulet once more, a helmet's Thorn suffix Thorn once; the Rime ring in the bag adds nothing, nor does
    /// a Rime ring worn in a slot it does not fit (the feet), and a theme at 0 is not listed.
    /// </summary>
    [Fact]
    public async Task Return_the_characters_saved_stats_with_the_resonance_it_wears()
    {
        var id = new CharacterId(42);
        Dictionary<ulong, ItemTemplate> seeded;
        using (WorldDbContext world = _world.CreateWorld(new WorldId(1)))
            seeded = world.ItemTemplates.AsEnumerable().Where(t => t.Id.Value is 36 or 68 or 69).ToDictionary(t => t.Id.Value);

        static ItemInstance Copy(CharacterId owner, ItemTemplate template, params int[] modifierIds)
        {
            var itemId = new ItemInstanceId(Guid.CreateVersion7());
            return new ItemInstance
            {
                Identified = true,
                Id = itemId,
                TemplateId = template.Id,
                CharacterId = owner,
                Count = 1,
                Rarity = Avalon.Common.ItemRarity.Magic,
                ItemLevel = 30,
                Modifiers = [.. modifierIds.Select((modifierId, ordinal) => new ItemInstanceModifier
                {
                    Id = Guid.CreateVersion7(), ItemInstanceId = itemId, ModifierId = modifierId, Ordinal = (byte)ordinal,
                })],
            };
        }

        ItemInstance ring = Copy(id, seeded[68], Seeded("P8", Avalon.Network.Packets.Combat.DamageType.Fire, 9),
            Seeded("P10", Avalon.Network.Packets.Combat.DamageType.Fire, 9));
        ItemInstance amulet = Copy(id, seeded[69], Seeded("S5", Avalon.Network.Packets.Combat.DamageType.Fire, 9));
        ItemInstance helmet = Copy(id, seeded[36], Seeded("S10", null, 9));
        ItemInstance bagRing = Copy(id, seeded[68], Seeded("P8", Avalon.Network.Packets.Combat.DamageType.Cold, 9));
        ItemInstance footRing = Copy(id, seeded[68], Seeded("P8", Avalon.Network.Packets.Combat.DamageType.Cold, 9));

        ICharacterInventoryRepository slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterInventory>
        {
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Equipment, Slot = EquipmentSlots.Finger1, ItemId = ring.Id },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Equipment, Slot = EquipmentSlots.Neck, ItemId = amulet.Id },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Equipment, Slot = EquipmentSlots.Head, ItemId = helmet.Id },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 0, ItemId = bagRing.Id },
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Equipment, Slot = EquipmentSlots.Feet, ItemId = footRing.Id },
        });
        IItemInstanceRepository items = Substitute.For<IItemInstanceRepository>();
        items.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(new List<ItemInstance> { ring, amulet, helmet, bagRing, footRing });
        IItemTemplateRepository templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(seeded.Values.ToList());

        ICharacterStatsRepository stats = Substitute.For<ICharacterStatsRepository>();
        stats.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new CharacterStats
        {
            CharacterId = id,
            MaxHealth = 320,
            MaxPower1 = 100,
            MaxPower2 = 5,
            Stamina = 26,
            Strength = 27,
            Agility = 23,
            Intellect = 20,
            Armor = 12,
            BlockPct = 5f,
            CritPct = 5.5f,
        });

        CharacterStatsDto? dto = await StatsService(stats, slots, items, templates).GetStatsAsync(id);

        // The world's step over the same worn items and the whole seeded table.
        var counted = new ResonanceCounts();
        counted.Use((await Modifiers().GetCatalogAsync(CancellationToken.None)).Resonance);
        CharacterStatsCalculator.CountResonance(
            [.. new[] { ring, amulet, helmet }.Select(i => new WornItem(seeded[i.TemplateId.Value], default, i.Modifiers, null))],
            counted);
        List<(int, int)> worldCounts = [];
        foreach (ThemeCount theme in counted)
            worldCounts.Add((theme.ThemeId, theme.Count));

        Assert.NotNull(dto);
        Assert.Equal(42u, dto!.CharacterId);
        Assert.Equal(320u, dto.MaxHealth);
        Assert.Equal(100u, dto.MaxPower1);
        Assert.Equal(5u, dto.MaxPower2);
        Assert.Equal(26u, dto.Stamina);
        Assert.Equal(27u, dto.Strength);
        Assert.Equal(23u, dto.Agility);
        Assert.Equal(20u, dto.Intellect);
        Assert.Equal(12u, dto.Armor);
        Assert.Equal(5f, dto.BlockPct);
        Assert.Equal(5.5f, dto.CritPct);
        Assert.Equal([(ThemeId("Ember"), 2), (ThemeId("Thorn"), 1)], dto.Resonance.Select(r => ((int?)r.ThemeId, r.Count)));
        Assert.Equal(worldCounts, dto.Resonance.Select(r => (r.ThemeId, r.Count)));
    }

    /// <summary>A character the world server has never saved stats for has none to return (#676).</summary>
    [Fact]
    public async Task Return_no_stats_when_the_character_has_none_saved()
    {
        ICharacterStatsRepository stats = Substitute.For<ICharacterStatsRepository>();
        stats.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns((CharacterStats?)null);

        Assert.Null(await StatsService(stats, Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<IItemTemplateRepository>()).GetStatsAsync(new CharacterId(42)));
    }

    private CharacterService StatsService(ICharacterStatsRepository stats, ICharacterInventoryRepository slots,
        IItemInstanceRepository items, IItemTemplateRepository templates) =>
        new(Substitute.For<ICharacterRepository>(), slots,
            items, Substitute.For<ICharacterTalentRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), templates,
            Modifiers(), stats,
            Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>(), Substitute.For<ICombatDataRepository>(),
            Substitute.For<ITalentTreeRepository>(), Substitute.For<IQuestRepository>());

    /// <summary>#714: the saved quest rows, each held quest with its own counts, by quest and objective id.</summary>
    [Fact]
    public async Task Map_the_saved_quest_log()
    {
        var id = new CharacterId(42);
        var accepted = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var completed = new DateTime(2026, 9, 29, 8, 30, 0, DateTimeKind.Utc);
        ICharacterQuestRepository quests = Substitute.For<ICharacterQuestRepository>();
        quests.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new CharacterQuestRows(
            [
                new CharacterQuest { CharacterId = id, QuestId = 3, State = Avalon.Domain.Characters.CharacterQuestState.Active, Stage = 1, AcceptedAt = accepted },
                new CharacterQuest { CharacterId = id, QuestId = 2, State = Avalon.Domain.Characters.CharacterQuestState.ReadyToTurnIn, Stage = 0, AcceptedAt = accepted.AddHours(-1) },
            ],
            [
                new CharacterQuestObjective { CharacterId = id, QuestId = 3, ObjectiveId = 302, Progress = 2 },
                new CharacterQuestObjective { CharacterId = id, QuestId = 3, ObjectiveId = 301, Progress = 3 },
                new CharacterQuestObjective { CharacterId = id, QuestId = 2, ObjectiveId = 201, Progress = 4 },
            ],
            [new CharacterCompletedQuest { CharacterId = id, QuestId = 1, CompletedAt = completed, LastCompletedAt = completed.AddDays(2), CompletionCount = 3 }]));

        CharacterQuestLogDto log = await QuestService(quests).GetQuestLogAsync(id);

        Assert.Equal(42u, log.CharacterId);
        Assert.Equal([2u, 3u], log.Active.Select(q => q.QuestId));
        CharacterActiveQuestDto ready = log.Active[0];
        Assert.Equal((Avalon.Api.Contract.CharacterQuestState.ReadyToTurnIn, 0, accepted.AddHours(-1)), (ready.State, ready.Stage, ready.AcceptedAt));
        Assert.Equal([(201u, 4u)], ready.Objectives.Select(o => (o.ObjectiveId, o.Progress)));
        CharacterActiveQuestDto active = log.Active[1];
        Assert.Equal((Avalon.Api.Contract.CharacterQuestState.Active, 1, accepted), (active.State, active.Stage, active.AcceptedAt));
        Assert.Equal([(301u, 3u), (302u, 2u)], active.Objectives.Select(o => (o.ObjectiveId, o.Progress)));
        CharacterCompletedQuestDto done = Assert.Single(log.Completed);
        Assert.Equal((1u, completed, completed.AddDays(2), 3u), (done.QuestId, done.CompletedAt, done.LastCompletedAt, done.CompletionCount));
    }

    [Fact]
    public async Task Return_an_empty_quest_log_for_a_character_with_no_quest_rows()
    {
        ICharacterQuestRepository quests = Substitute.For<ICharacterQuestRepository>();
        quests.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(CharacterQuestRows.None);

        CharacterQuestLogDto log = await QuestService(quests).GetQuestLogAsync(new CharacterId(7));

        Assert.Equal(7u, log.CharacterId);
        Assert.Empty(log.Active);
        Assert.Empty(log.Completed);
    }

    [Fact]
    public async Task List_a_characters_saved_auras_by_slot()
    {
        ICharacterAuraRepository auras = Substitute.For<ICharacterAuraRepository>();
        DateTime applied = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        auras.GetByCharacterIdAsync(new CharacterId(42), Arg.Any<CancellationToken>()).Returns(
        [
            new CharacterAura { CharacterId = new CharacterId(42), Slot = 0, AuraId = 1,
                CasterGuid = new ObjectGuid(ObjectType.Character, 9).RawValue, SourceAbilityId = 203,
                Stacks = 3, RemainingMs = 4000, DurationMs = 12000, TicksLeft = 2, AppliedAt = applied },
            new CharacterAura { CharacterId = new CharacterId(42), Slot = 1, AuraId = 5, CasterGuid = 0, SourceAbilityId = null,
                Stacks = 1, RemainingMs = 30000, DurationMs = 30000, TicksLeft = 0, AppliedAt = applied },
        ]);
        var service = new CharacterService(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterTalentRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(), auras,
            Substitute.For<ICombatDataRepository>(),
            Substitute.For<ITalentTreeRepository>(), Substitute.For<IQuestRepository>());

        CharacterAurasDto dto = await service.GetAurasAsync(new CharacterId(42));

        Assert.Equal(42u, dto.CharacterId);
        Assert.Equal(2, dto.Auras.Count);
        CharacterAuraDto bleed = dto.Auras[0];
        Assert.Equal((1u, (uint?)9u, (uint?)203u, 3, 4000u, 12000u, 2, applied), (bleed.AuraId, bleed.CasterCharacterId,
            bleed.SourceAbilityId, bleed.Stacks, bleed.RemainingMs, bleed.DurationMs, bleed.TicksLeft, bleed.AppliedAt));
        // A caster nobody knows is nobody.
        Assert.Equal((5u, (uint?)null, (uint?)null), (dto.Auras[1].AuraId, dto.Auras[1].CasterCharacterId,
            dto.Auras[1].SourceAbilityId));
    }

    [Fact]
    public async Task List_no_auras_for_a_character_with_none()
    {
        ICharacterAuraRepository auras = Substitute.For<ICharacterAuraRepository>();
        auras.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns([]);
        var service = new CharacterService(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterTalentRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(), auras,
            Substitute.For<ICombatDataRepository>(),
            Substitute.For<ITalentTreeRepository>(), Substitute.For<IQuestRepository>());

        CharacterAurasDto dto = await service.GetAurasAsync(new CharacterId(7));

        Assert.Equal(7u, dto.CharacterId);
        Assert.Empty(dto.Auras);
    }

    private static CharacterService QuestService(ICharacterQuestRepository quests) =>
        new(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterTalentRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(), quests, Substitute.For<ICharacterAuraRepository>(),
            Substitute.For<ICombatDataRepository>(),
            Substitute.For<ITalentTreeRepository>(), Substitute.For<IQuestRepository>());
}
