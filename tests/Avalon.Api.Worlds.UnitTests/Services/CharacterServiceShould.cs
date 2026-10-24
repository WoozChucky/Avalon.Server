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

namespace Avalon.Api.Worlds.UnitTests.Services;

public class CharacterServiceShould
{
    /// <summary>The seeded world's modifier catalog (#1179), read as the API reads it.</summary>
    private static readonly Lazy<ItemModifierCatalog> s_catalog = new(() =>
    {
        using var sqlite = new SqliteWorlds(1);
        return new ItemModifierRepository(
                new DelegateDbContextFactory<WorldDbContext>(() => sqlite.CreateWorld(new Avalon.Domain.Auth.WorldId(1))),
                NullLoggerFactory.Instance)
            .GetCatalogAsync(CancellationToken.None).GetAwaiter().GetResult();
    });

    private static IItemModifierRepository Modifiers(ItemModifierCatalog catalog)
    {
        IItemModifierRepository modifiers = Substitute.For<IItemModifierRepository>();
        modifiers.GetCatalogAsync(Arg.Any<CancellationToken>()).Returns(catalog);
        return modifiers;
    }

    /// <summary>A seeded affix's id, named by its family's code and tier: no test holds an id (#1179).</summary>
    private static int Seeded(string code, byte tier)
    {
        int group = ItemModifierSeed.Groups().Single(g => g.Code == code && g.Element is null).Id;
        return ItemModifierSeed.Modifiers().Single(m => m.GroupId == group && m.Tier == tier).Id;
    }

    /// <summary>The seeded implicit's id of a base, named by the base.</summary>
    private static int ImplicitOf(ulong template) =>
        ItemModifierSeed.TemplateLinks().Single(link => link.TemplateId.Value == template).ModifierId;

    /// <summary>
    /// Instances live in the Character database and templates in the World database, so the
    /// template is looked up by id rather than joined. The copy's own rarity, item level and modifiers come from the
    /// instance (#1179), in the order the item shows them, whatever order the rows were read in, each with its kind
    /// from the world's catalog; a magic copy is named by its prefix, and its implicit names nothing.
    /// </summary>
    [Fact]
    public async Task Look_up_each_items_template_in_the_world_database()
    {
        int hale = Seeded("P11", 1);
        int armour = ImplicitOf(37);
        var id = new CharacterId(42);
        var itemId = new ItemInstanceId(Guid.CreateVersion7());

        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Holder" });

        ICharacterInventoryRepository slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterInventory>
        {
            new() { CharacterId = id, Container = Avalon.Common.InventoryType.Bag, Slot = 3, ItemId = itemId },
        });

        IItemInstanceRepository items = Substitute.For<IItemInstanceRepository>();
        items.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<ItemInstance>
        {
            new()
            {
                Id = itemId, TemplateId = new ItemTemplateId(37), CharacterId = id, Count = 4, Durability = 7,
                Rarity = Avalon.Common.ItemRarity.Magic, ItemLevel = 7,
                Modifiers =
                [
                    new ItemInstanceModifier
                    {
                        Id = Guid.CreateVersion7(), ItemInstanceId = itemId, ModifierId = hale, Ordinal = 1,
                        Values = [new ItemInstanceModifierValue { Index = 0, Stat = Avalon.Domain.World.StatType.Health, Value = 5 }],
                    },
                    new ItemInstanceModifier
                    {
                        Id = Guid.CreateVersion7(), ItemInstanceId = itemId, ModifierId = armour, Ordinal = 0,
                        Values =
                        [
                            new ItemInstanceModifierValue { Index = 1, Stat = Avalon.Domain.World.StatType.AddedColdDamage, Value = 2, Value2 = 5 },
                            new ItemInstanceModifierValue { Index = 0, Stat = Avalon.Domain.World.StatType.Armor, Value = 8 },
                        ],
                    },
                ],
            },
        });

        IItemTemplateRepository templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemTemplate>
            {
                new() { Id = new ItemTemplateId(37), Name = "Ironbound Chestguard", Rarity = Avalon.Common.ItemRarity.Common, DisplayId = 12 },
            });

        var service = new CharacterService(characters, slots, items,
            Substitute.For<ICharacterAbilityRepository>(), Substitute.For<IAbilityTemplateRepository>(), templates,
            Modifiers(s_catalog.Value), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>(), Substitute.For<ICombatDataRepository>());

        CharacterInventoryDto? inventory = await service.GetInventoryAsync(id);

        CharacterInventoryItemDto item = Assert.Single(inventory!.Items);
        Assert.Equal(itemId.Value, item.ItemId);
        Assert.Equal(4u, item.Count);
        Assert.Equal(7u, item.Durability);
        Assert.NotNull(item.Template);
        Assert.Equal(37ul, item.Template!.Id);
        Assert.Equal("Ironbound Chestguard", item.Template.Name);
        Assert.Equal(12u, item.Template.DisplayId);
        Assert.Equal((Avalon.Api.Contract.ItemRarity.Magic, (ushort)7), (item.Rarity, item.ItemLevel));
        Assert.Equal("Hale Ironbound Chestguard", item.Name);
        Assert.Equal(
            $"{armour} Implicit [Armor 8 , AddedColdDamage 2 5] {hale} Prefix [Health 5 ]",
            string.Join(' ', item.Modifiers.Select(m =>
                $"{m.ModifierId} {m.Kind} [{string.Join(", ", m.Values.Select(v => $"{v.Stat} {v.Value} {v.Value2}"))}]")));
    }

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
            new() { Id = itemId, TemplateId = new ItemTemplateId(1), CharacterId = id, Count = 40 },
        });

        IItemTemplateRepository templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemTemplate> { new() { Id = new ItemTemplateId(1), Name = "Health Potion", Slot = slot } });

        var service = new CharacterService(characters, slots, items,
            Substitute.For<ICharacterAbilityRepository>(), Substitute.For<IAbilityTemplateRepository>(), templates,
            Modifiers(s_catalog.Value), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>(), Substitute.For<ICombatDataRepository>());

        CharacterInventoryDto? inventory = await service.GetInventoryAsync(id);

        CharacterInventoryItemDto item = Assert.Single(inventory!.Items);
        Assert.Equal((Avalon.Api.Contract.ItemSlotType?)slot, item.Template!.SlotType);
    }

    private static CharacterService AbilityService(CharacterId id, AbilityTemplate ability, CharacterStats? stats,
        IReadOnlyList<ItemInstance> worn, IReadOnlyList<ItemTemplate> templateRows, CombatFormula formula)
    {
        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Caster", Level = 1 });

        ICharacterAbilityRepository rows = Substitute.For<ICharacterAbilityRepository>();
        rows.GetCharacterAbilitiesAsync(id, Arg.Any<CancellationToken>())
            .Returns(new List<CharacterAbility> { new() { CharacterId = id, AbilityId = ability.Id } });
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

        return new CharacterService(characters, slots, instances, rows, abilities, templates,
            Substitute.For<IItemModifierRepository>(), statsRepository,
            Substitute.For<ICharacterQuestRepository>(), Substitute.For<ICharacterAuraRepository>(), combat);
    }

    /// <summary>
    /// A saved copy of <paramref name="template" /> with one modifier per value, as the Character database loads it: its
    /// modifiers and their values.
    /// </summary>
    private static ItemInstance Saved(ItemTemplate template,
        params (Avalon.Domain.World.StatType Stat, int Value, int? Value2)[] values)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        return new ItemInstance
        {
            Id = id,
            TemplateId = template.Id,
            Count = 1,
            Modifiers = [.. values.Select((v, ordinal) =>
            {
                var modifierId = Guid.CreateVersion7();
                return new ItemInstanceModifier
                {
                    Id = modifierId,
                    ItemInstanceId = id,
                    ModifierId = 1,
                    Ordinal = (byte)ordinal,
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
    /// #1178, #1179 (Review Focus 5): the worlds service advertises exactly what the world server sends for the same saved
    /// character: AbilityAmounts over the stats calculator's attacker for the same row and worn items, as the world builds
    /// it at select. A saved level-1 Warrior (Strength 23: #1179's bases carry no attributes) in the starter sword and
    /// armour: Cleave rolls 4..7 at 315 % effectiveness and 80.5 % increased physical attack damage, 22..39. With the same
    /// ring on both fingers (10 % increased physical damage each), both count, as in the world: 100.5 %, 25..44. With the
    /// sword's own added fire 2..5 and a Copper Band's added physical damage to attacks 1..3, each read from its saved
    /// instance's modifiers: 34..72.
    /// </summary>
    [Theory]
    [InlineData("starter gear", 22u, 39u)]
    [InlineData("two rings", 25u, 44u)]
    [InlineData("a modified sword and a global ring", 34u, 72u)]
    public async Task Advertise_the_amounts_the_world_sends_for_the_same_saved_character(string gear, uint min, uint max)
    {
        var id = new CharacterId(42);
        using var sqlite = new SqliteWorlds(1);
        using WorldDbContext world = sqlite.CreateWorld(new WorldId(1));
        ClassLevelStat row = world.ClassLevelStats.AsEnumerable()
            .Single(r => r.Class == CharacterClass.Warrior && r.Level == 1);
        ClassStatFactors factors = world.ClassStatFactors.AsEnumerable().Single(f => f.Class == CharacterClass.Warrior);
        CombatFormula formula = world.CombatFormulas.Single();
        var templates = world.ItemTemplates.AsEnumerable().ToDictionary(t => t.Id.Value);
        List<ItemInstance> worn = [.. new ulong[] { 36, 37, 38, 39, 40 }.Select(t => Saved(templates[t]))];
        switch (gear)
        {
            case "starter gear":
                worn.Add(Saved(templates[32]));
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
                worn.AddRange([Saved(templates[32]), Saved(ring), Saved(ring)]);
                break;
            case "a modified sword and a global ring":
                worn.Add(Saved(templates[32], (Avalon.Domain.World.StatType.AddedFireDamage, 2, 5)));
                worn.Add(Saved(templates[68], (Avalon.Domain.World.StatType.AddedPhysicalDamageToAttacks, 1, 3)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gear), gear, null);
        }

        List<ItemTemplate> wornTemplates = [.. worn.Select(item => templates[item.TemplateId.Value])];
        AbilityTemplate cleave = world.AbilityTemplates.AsEnumerable().Single(a => a.Id.Value == 200);
        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row,
            [.. worn.Select((item, i) => new WornItem(wornTemplates[i], item.Modifiers))], factors, formula);
        AbilityAmount sent = AbilityAmounts.For(stats.AttackerAt(1), AbilityMetadataMapper.From(cleave), default);

        CharacterAbilitiesDto? result = await AbilityService(id, cleave, stats.ToRow(id), worn, wornTemplates, formula)
            .GetAbilitiesAsync(id);

        CharacterAbilityAmountDto amount = Assert.Single(result!.Abilities).Amount!;
        Assert.Equal(23u, stats.Strength);
        Assert.Equal((Avalon.Api.Contract.AbilityAmountKind.Damage, min, max), (amount.Kind, amount.Min, amount.Max));
        Assert.Equal(((Avalon.Api.Contract.AbilityAmountKind)sent.Kind, sent.Min, sent.Max),
            (amount.Kind, amount.Min, amount.Max));
    }

    /// <summary>No saved stats and no weapon: the bare hand's 3..6 at Cleave's 315 %, with no offence, 9..18.</summary>
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
            CombatSeed.Formula()).GetAbilitiesAsync(id);

        CharacterAbilityAmountDto amount = Assert.Single(result!.Abilities).Amount!;
        Assert.Equal((Avalon.Api.Contract.AbilityAmountKind.Damage, 9u, 18u), (amount.Kind, amount.Min, amount.Max));
    }

    /// <summary>A character's abilities carry the pool each cost is spent from (#652).</summary>
    [Fact]
    public async Task Return_each_abilitys_cost_power_type()
    {
        var id = new CharacterId(42);

        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Caster" });

        ICharacterAbilityRepository rows = Substitute.For<ICharacterAbilityRepository>();
        rows.GetCharacterAbilitiesAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterAbility>
        {
            new() { CharacterId = id, AbilityId = new AbilityId(210) },
            new() { CharacterId = id, AbilityId = new AbilityId(211) },
        });

        IAbilityTemplateRepository abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.GetByIdsAsync(Arg.Any<IEnumerable<AbilityId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AbilityTemplate>
            {
                new() { Id = new AbilityId(210), Name = "Firebolt", Cost = 0 },
                new()
                {
                    Id = new AbilityId(211), Name = "Flame Burst", Cost = 20,
                    CostPowerType = Avalon.Network.Packets.State.PowerType.Mana,
                },
            });

        ICombatDataRepository combat = Substitute.For<ICombatDataRepository>();
        combat.GetFormulasAsync(Arg.Any<CancellationToken>()).Returns(new List<CombatFormula> { CombatSeed.Formula() });

        var service = new CharacterService(characters, Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), rows, abilities, Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>(), combat);

        CharacterAbilitiesDto? result = await service.GetAbilitiesAsync(id);

        Assert.Collection(result!.Abilities,
            free =>
            {
                Assert.Equal(0u, free.Template!.Cost);
                Assert.Equal(PowerType.None, free.Template.CostPowerType);
            },
            costed =>
            {
                Assert.Equal(20u, costed.Template!.Cost);
                Assert.Equal(PowerType.Mana, costed.Template.CostPowerType);
            });
    }

    /// <summary>The stats the world server last saved for the character (#676).</summary>
    [Fact]
    public async Task Return_the_characters_saved_stats()
    {
        var id = new CharacterId(42);
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

        CharacterStatsDto? dto = await StatsService(stats).GetStatsAsync(id);

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
    }

    /// <summary>A character the world server has never saved stats for has none to return (#676).</summary>
    [Fact]
    public async Task Return_no_stats_when_the_character_has_none_saved()
    {
        ICharacterStatsRepository stats = Substitute.For<ICharacterStatsRepository>();
        stats.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns((CharacterStats?)null);

        Assert.Null(await StatsService(stats).GetStatsAsync(new CharacterId(42)));
    }

    private static CharacterService StatsService(ICharacterStatsRepository stats) =>
        new(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), stats,
            Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>(), Substitute.For<ICombatDataRepository>());

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
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(), auras,
            Substitute.For<ICombatDataRepository>());

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
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(), auras,
            Substitute.For<ICombatDataRepository>());

        CharacterAurasDto dto = await service.GetAurasAsync(new CharacterId(7));

        Assert.Equal(7u, dto.CharacterId);
        Assert.Empty(dto.Auras);
    }

    private static CharacterService QuestService(ICharacterQuestRepository quests) =>
        new(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IItemModifierRepository>(), Substitute.For<ICharacterStatsRepository>(), quests, Substitute.For<ICharacterAuraRepository>(),
            Substitute.For<ICombatDataRepository>());
}
