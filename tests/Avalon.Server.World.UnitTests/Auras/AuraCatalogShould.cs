using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>The Auras reload area: every valid row loads, a bad one is refused by name and the rest still load.</summary>
public class AuraCatalogShould
{
    private static AuraCatalog Of(params AuraTemplate[] rows) =>
        new(rows, name => name == nameof(RecordingAuraScript) ? typeof(RecordingAuraScript) : null, NullLoggerFactory.Instance);

    [Fact]
    public void Load_every_valid_aura_and_refuse_a_bad_one_by_name()
    {
        AuraTemplate bad = AuraTestData.Burn();
        bad.TickIntervalMs = 0;

        AuraCatalog catalog = Of(AuraTestData.Bleed(), bad, AuraTestData.Fortified());

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(new AuraId(901), out _));
        Assert.False(catalog.TryGet(new AuraId(902), out _));
        AuraRefusal refusal = Assert.Single(catalog.Refused);
        Assert.Equal("aura 902 'Burn': a periodic aura needs a TickIntervalMs above 0", refusal.ToString());
        Assert.Equal("2 auras, 1 refused", catalog.Describe());
    }

    [Fact]
    public void Refuse_an_aura_whose_script_is_not_loaded()
    {
        AuraCatalog catalog = Of(AuraTestData.Scripted(AuraTestData.Bleed(), "NoSuchScript"),
            AuraTestData.Scripted(AuraTestData.Renew(), nameof(RecordingAuraScript)));

        Assert.Equal("aura 901 'Bleed': names aura script 'NoSuchScript', which is not loaded",
            Assert.Single(catalog.Refused).ToString());
        Assert.True(catalog.TryGet(new AuraId(904), out _));
    }

    [Fact]
    public void Refuse_a_negative_base_damage_coefficient_and_load_a_positive_one()
    {
        AuraTemplate bad = AuraTestData.Bleed();
        bad.BaseDamageCoefficient = -1f;
        AuraTemplate poison = AuraTestData.Burn();
        poison.BaseDamageCoefficient = 1f;

        AuraCatalog catalog = Of(bad, poison);

        Assert.Equal("aura 901 'Bleed': BaseDamageCoefficient -1 is not a finite value of 0 or more",
            Assert.Single(catalog.Refused).ToString());
        Assert.True(catalog.TryGet(new AuraId(902), out AuraTemplate? loaded));
        Assert.Equal(1f, loaded!.BaseDamageCoefficient);
    }

    [Fact]
    public void Be_empty_before_anything_loads() => Assert.Equal(0, AuraCatalog.Empty.Count);

    private static AbilityTemplate Rend(AuraId? aura)
    {
        AbilityTemplate rend = AbilityTestData.Cone(203);
        rend.AuraId = aura;
        return rend;
    }

    [Fact]
    public void Refuse_an_ability_naming_an_aura_the_catalog_does_not_hold()
    {
        AbilityTemplate unknown = AbilityTestData.Cone(204);
        unknown.AuraId = new AuraId(999);

        var abilities = new AbilityCatalog([Rend(new AuraId(901)), unknown], NullLoggerFactory.Instance, Of(AuraTestData.Bleed()));

        Assert.True(abilities.TryGet(new AbilityId(203), out _));
        Assert.Equal("ability 204 'Cone': names aura 999, which is missing or refused", Assert.Single(abilities.Refused).ToString());
    }

    [Fact]
    public void Refuse_an_ability_whose_aura_does_not_fit_what_it_affects()
    {
        AbilityTemplate heal = AbilityTestData.HealCircle(233);
        heal.AuraId = new AuraId(901);   // a harmful aura on an Ally ability

        var abilities = new AbilityCatalog([heal], NullLoggerFactory.Instance, Of(AuraTestData.Bleed()));

        Assert.Equal("ability 233 'Heal circle': its aura 901 'Bleed' is Harmful, which an Ally ability cannot apply",
            Assert.Single(abilities.Refused).ToString());
    }

    [Fact]
    public void Leave_auras_unchecked_without_an_aura_catalog() =>
        Assert.Empty(new AbilityCatalog([Rend(new AuraId(999))], NullLoggerFactory.Instance).Refused);

    /// <summary>The Auras area is reloadable, and the Abilities area checks its aura links against the auras it read.</summary>
    [Fact]
    public async Task Reload_the_auras_and_check_the_abilities_against_them()
    {
        List<AuraTemplate> auras = [AuraTestData.Bleed()];
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            auras: () => auras, abilities: () => [Rend(new AuraId(901))]));

        Assert.True(data.Auras.TryGet(new AuraId(901), out _));
        Assert.True(data.Abilities.TryGet(new AbilityId(203), out _));

        auras = [AuraTestData.Fortified()];
        data.Apply(await data.PrepareAsync(ReloadArea.Auras));
        data.Apply(await data.PrepareAsync(ReloadArea.Abilities));

        Assert.False(data.Auras.TryGet(new AuraId(901), out _));
        Assert.True(data.Auras.TryGet(new AuraId(905), out _));
        Assert.False(data.Abilities.TryGet(new AbilityId(203), out _));
    }
}
