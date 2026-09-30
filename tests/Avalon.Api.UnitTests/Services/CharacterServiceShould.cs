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
            Substitute.For<ICharacterStatsRepository>());

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
            Substitute.For<ICharacterStatsRepository>());

        CharacterInventoryDto? inventory = await service.GetInventoryAsync(id);

        CharacterInventoryItemDto item = Assert.Single(inventory!.Items);
        Assert.Equal((Avalon.Api.Contract.ItemSlotType?)slot, item.Template!.SlotType);
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
            Substitute.For<ICharacterStatsRepository>());

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
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(), stats);
}
