using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public class CharacterServiceShould
{
    /// <summary>
    /// Instances live in the Character database and templates in the World database, so the
    /// template is looked up by id rather than joined. The DTO the endpoint returns is unchanged.
    /// </summary>
    [Fact]
    public async Task Look_up_each_items_template_in_the_world_database()
    {
        var id = new CharacterId(42);
        var itemId = new ItemInstanceId(Guid.CreateVersion7());

        var characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Holder" });

        var slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterInventory>
        {
            new() { CharacterId = id, Container = Avalon.World.Public.Enums.InventoryType.Bag, Slot = 3, ItemId = itemId },
        });

        var items = Substitute.For<IItemInstanceRepository>();
        items.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<ItemInstance>
        {
            new() { Id = itemId, TemplateId = new ItemTemplateId(9), CharacterId = id, Count = 4, Durability = 7 },
        });

        var templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemTemplate>
            {
                new() { Id = new ItemTemplateId(9), Name = "Lantern", Rarity = Avalon.Domain.World.ItemRarity.Rare, DisplayId = 12 },
            });

        var service = new CharacterService(characters, slots, items,
            Substitute.For<ICharacterAbilityRepository>(), Substitute.For<IAbilityTemplateRepository>(), templates,
            Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>());

        CharacterInventoryDto? inventory = await service.GetInventoryAsync(id);

        CharacterInventoryItemDto item = Assert.Single(inventory!.Items);
        Assert.Equal(itemId.Value, item.ItemId);
        Assert.Equal(4u, item.Count);
        Assert.Equal(7u, item.Durability);
        Assert.NotNull(item.Template);
        Assert.Equal(9ul, item.Template!.Id);
        Assert.Equal("Lantern", item.Template.Name);
        Assert.Equal(12u, item.Template.DisplayId);
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

        var characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Holder" });

        var slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterInventory>
        {
            new() { CharacterId = id, Container = Avalon.World.Public.Enums.InventoryType.Bag, Slot = 0, ItemId = itemId },
        });

        var items = Substitute.For<IItemInstanceRepository>();
        items.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new List<ItemInstance>
        {
            new() { Id = itemId, TemplateId = new ItemTemplateId(1), CharacterId = id, Count = 40 },
        });

        var templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemTemplate> { new() { Id = new ItemTemplateId(1), Name = "Health Potion", Slot = slot } });

        var service = new CharacterService(characters, slots, items,
            Substitute.For<ICharacterAbilityRepository>(), Substitute.For<IAbilityTemplateRepository>(), templates,
            Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>());

        CharacterInventoryDto? inventory = await service.GetInventoryAsync(id);

        CharacterInventoryItemDto item = Assert.Single(inventory!.Items);
        Assert.Equal((Avalon.Api.Contract.ItemSlotType?)slot, item.Template!.SlotType);
    }

    private static CharacterService AbilityService(CharacterId id, AbilityTemplate ability, CharacterStats? stats,
        ItemTemplate? mainHand)
    {
        var characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Caster" });

        var rows = Substitute.For<ICharacterAbilityRepository>();
        rows.GetCharacterAbilitiesAsync(id, Arg.Any<CancellationToken>())
            .Returns(new List<CharacterAbility> { new() { CharacterId = id, AbilityId = ability.Id } });
        var abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.GetByIdsAsync(Arg.Any<IEnumerable<AbilityId>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AbilityTemplate> { ability });

        var weaponId = new ItemInstanceId(Guid.CreateVersion7());
        var slots = Substitute.For<ICharacterInventoryRepository>();
        slots.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(mainHand is null
            ? new List<CharacterInventory>()
            : new List<CharacterInventory>
            {
                new() { CharacterId = id, Container = Avalon.World.Public.Enums.InventoryType.Equipment, Slot = 9, ItemId = weaponId },
            });
        var instances = Substitute.For<IItemInstanceRepository>();
        instances.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(mainHand is null
            ? new List<ItemInstance>()
            : new List<ItemInstance> { new() { Id = weaponId, TemplateId = mainHand.Id, CharacterId = id, Count = 1 } });
        var templates = Substitute.For<IItemTemplateRepository>();
        templates.GetByIdsAsync(Arg.Any<IEnumerable<ItemTemplateId>>(), Arg.Any<CancellationToken>())
            .Returns(mainHand is null ? new List<ItemTemplate>() : new List<ItemTemplate> { mainHand });

        var statsRepository = Substitute.For<ICharacterStatsRepository>();
        statsRepository.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(stats);

        return new CharacterService(characters, slots, instances, rows, abilities, templates, statsRepository,
            Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>());
    }

    private static AbilityTemplate Cleave() => new()
    {
        Id = new AbilityId(210), Name = "Cleave", ScriptName = "ConeAbilityScript",
        Affects = Avalon.Network.Packets.Abilities.AbilityAffects.Hostile, EffectValue = 10,
        ScalingStat = Avalon.World.Public.Abilities.ScalingStat.Attack, ScalingCoefficient = 0.5f, BaseDamageCoefficient = 1f,
        Effects = Avalon.World.Public.Enums.SpellEffect.Damage,
    };

    /// <summary>10 + 0.5 × 40 attack + 1 × (24..28) main hand: 54..58, the world server's own example (#669).</summary>
    [Fact]
    public async Task Compute_each_abilitys_per_hit_amount_from_saved_stats_and_the_main_hand()
    {
        var id = new CharacterId(42);
        ItemTemplate axe = new()
        {
            Id = new ItemTemplateId(5), Name = "Axe", Slot = Avalon.Domain.World.ItemSlotType.MainHand,
            DamageMin1 = 24, DamageMax1 = 28,
        };

        CharacterAbilitiesDto? result = await AbilityService(id, Cleave(),
            new CharacterStats { CharacterId = id, AttackDamage = 40 }, axe).GetAbilitiesAsync(id);

        CharacterAbilityAmountDto amount = Assert.Single(result!.Abilities).Amount!;
        Assert.Equal((Avalon.Api.Contract.AbilityAmountKind.Damage, 54u, 58u), (amount.Kind, amount.Min, amount.Max));
    }

    [Fact]
    public async Task Compute_amounts_without_stats_or_a_weapon()
    {
        var id = new CharacterId(42);

        CharacterAbilitiesDto? result = await AbilityService(id, Cleave(), stats: null, mainHand: null).GetAbilitiesAsync(id);

        CharacterAbilityAmountDto amount = Assert.Single(result!.Abilities).Amount!;
        Assert.Equal((Avalon.Api.Contract.AbilityAmountKind.Damage, 10u, 10u), (amount.Kind, amount.Min, amount.Max));
    }

    [Fact]
    public async Task Report_no_amount_for_an_ability_another_script_runs()
    {
        var id = new CharacterId(42);
        AbilityTemplate charge = Cleave();
        charge.ScriptName = "ChargeAbilityScript";

        CharacterAbilitiesDto? result = await AbilityService(id, charge, stats: null, mainHand: null).GetAbilitiesAsync(id);

        Assert.Equal(Avalon.Api.Contract.AbilityAmountKind.None, Assert.Single(result!.Abilities).Amount!.Kind);
    }

    /// <summary>A character's abilities carry the pool each cost is spent from (#652).</summary>
    [Fact]
    public async Task Return_each_abilitys_cost_power_type()
    {
        var id = new CharacterId(42);

        var characters = Substitute.For<ICharacterRepository>();
        characters.FindByIdAsync(id, false, Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Characters.Character { Id = id, Name = "Caster" });

        var rows = Substitute.For<ICharacterAbilityRepository>();
        rows.GetCharacterAbilitiesAsync(id, Arg.Any<CancellationToken>()).Returns(new List<CharacterAbility>
        {
            new() { CharacterId = id, AbilityId = new AbilityId(210) },
            new() { CharacterId = id, AbilityId = new AbilityId(211) },
        });

        var abilities = Substitute.For<IAbilityTemplateRepository>();
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

        var service = new CharacterService(characters, Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), rows, abilities, Substitute.For<IItemTemplateRepository>(),
            Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>());

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
        var stats = Substitute.For<ICharacterStatsRepository>();
        stats.GetByCharacterIdAsync(id, Arg.Any<CancellationToken>()).Returns(new CharacterStats
        {
            CharacterId = id, MaxHealth = 320, MaxPower1 = 100, MaxPower2 = 5, Stamina = 26, Strength = 27,
            Agility = 23, Intellect = 20, Armor = 12, BlockPct = 5f, DodgePct = 3.664f, CritPct = 5.5f,
            AttackDamage = 54, AbilityDamage = 4,
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
        Assert.Equal(3.664f, dto.DodgePct);
        Assert.Equal(5.5f, dto.CritPct);
        Assert.Equal(54u, dto.AttackDamage);
        Assert.Equal(4u, dto.AbilityDamage);
    }

    /// <summary>A character the world server has never saved stats for has none to return (#676).</summary>
    [Fact]
    public async Task Return_no_stats_when_the_character_has_none_saved()
    {
        var stats = Substitute.For<ICharacterStatsRepository>();
        stats.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns((CharacterStats?)null);

        Assert.Null(await StatsService(stats).GetStatsAsync(new CharacterId(42)));
    }

    private static CharacterService StatsService(ICharacterStatsRepository stats) =>
        new(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(), stats,
            Substitute.For<ICharacterQuestRepository>(),
            Substitute.For<ICharacterAuraRepository>());

    /// <summary>#714: the saved quest rows, each held quest with its own counts, by quest and objective id.</summary>
    [Fact]
    public async Task Map_the_saved_quest_log()
    {
        var id = new CharacterId(42);
        var accepted = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var completed = new DateTime(2026, 9, 29, 8, 30, 0, DateTimeKind.Utc);
        var quests = Substitute.For<ICharacterQuestRepository>();
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
            [new CharacterCompletedQuest { CharacterId = id, QuestId = 1, CompletedAt = completed }]));

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
        Assert.Equal((1u, completed), (done.QuestId, done.CompletedAt));
    }

    [Fact]
    public async Task Return_an_empty_quest_log_for_a_character_with_no_quest_rows()
    {
        var quests = Substitute.For<ICharacterQuestRepository>();
        quests.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(CharacterQuestRows.None);

        CharacterQuestLogDto log = await QuestService(quests).GetQuestLogAsync(new CharacterId(7));

        Assert.Equal(7u, log.CharacterId);
        Assert.Empty(log.Active);
        Assert.Empty(log.Completed);
    }

    /// <summary>The contract enum is cast from the stored one, so the two must agree name for value.</summary>
    [Fact]
    public void Mirror_every_quest_state_by_name_and_value() =>
        Assert.Equal(
            Enum.GetValues<Avalon.Domain.Characters.CharacterQuestState>().Select(s => (s.ToString(), (int)s)),
            Enum.GetValues<Avalon.Api.Contract.CharacterQuestState>().Select(s => (s.ToString(), (int)s)));

    [Fact]
    public async Task List_a_characters_saved_auras_by_slot()
    {
        var auras = Substitute.For<ICharacterAuraRepository>();
        DateTime applied = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        auras.GetByCharacterIdAsync(new CharacterId(42), Arg.Any<CancellationToken>()).Returns(
        [
            new CharacterAura { CharacterId = new CharacterId(42), Slot = 0, AuraId = 1, CasterGuid = 9, SourceAbilityId = 203,
                Stacks = 3, RemainingMs = 4000, DurationMs = 12000, TicksLeft = 2, AppliedAt = applied },
            new CharacterAura { CharacterId = new CharacterId(42), Slot = 1, AuraId = 5, CasterGuid = 0, SourceAbilityId = null,
                Stacks = 1, RemainingMs = 30000, DurationMs = 30000, TicksLeft = 0, AppliedAt = applied },
        ]);
        var service = new CharacterService(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(), auras);

        CharacterAurasDto dto = await service.GetAurasAsync(new CharacterId(42));

        Assert.Equal(42u, dto.CharacterId);
        Assert.Equal(2, dto.Auras.Count);
        CharacterAuraDto bleed = dto.Auras[0];
        Assert.Equal((1u, 9ul, (uint?)203u, 3, 4000u, 12000u, 2, applied), (bleed.AuraId, bleed.CasterGuid, bleed.SourceAbilityId,
            bleed.Stacks, bleed.RemainingMs, bleed.DurationMs, bleed.TicksLeft, bleed.AppliedAt));
        // A caster nobody knows is shown as saved: 0.
        Assert.Equal((5u, 0ul, (uint?)null), (dto.Auras[1].AuraId, dto.Auras[1].CasterGuid, dto.Auras[1].SourceAbilityId));
    }

    [Fact]
    public async Task List_no_auras_for_a_character_with_none()
    {
        var auras = Substitute.For<ICharacterAuraRepository>();
        auras.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns([]);
        var service = new CharacterService(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>(), auras);

        CharacterAurasDto dto = await service.GetAurasAsync(new CharacterId(7));

        Assert.Equal(7u, dto.CharacterId);
        Assert.Empty(dto.Auras);
    }

    private static CharacterService QuestService(ICharacterQuestRepository quests) =>
        new(Substitute.For<ICharacterRepository>(), Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<ICharacterStatsRepository>(), quests, Substitute.For<ICharacterAuraRepository>());
}
