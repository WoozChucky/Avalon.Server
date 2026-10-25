using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Items;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// #1180 part A2: what the effect catalog leaves out rather than trust. Over one sound row (Burn on the hit target of a
/// Hit, typed Fire), one breach at a time: the row under test is logged and left out, and the sound row is kept. A reload
/// that turns a kept effect's aura helpful is refused when the effect applies it, by the aura system's fit check.
/// </summary>
public class ResonanceEffectCatalogShould
{
    private const int Sound = 1;
    private const int Subject = 2;
    private const uint Missing = 999;

    [Theory]
    [InlineData("its aura is not loaded")]
    [InlineData("a helpful aura on an enemy")]
    [InlineData("a harmful aura on the wearer")]
    [InlineData("a hit target on a Struck trigger")]
    [InlineData("the attacker on a Hit trigger")]
    [InlineData("a hit target on a Kill trigger")]
    [InlineData("a chain on the wearer")]
    [InlineData("a damaging aura without a damage type")]
    [InlineData("a damage type on a status")]
    [InlineData("an aura that stuns")]
    [InlineData("a chance of 0")]
    [InlineData("a cooldown of 0")]
    [InlineData("a second row for one theme, threshold and trigger")]
    [InlineData("an effect aura reloaded helpful")]
    public void Leave_out_what_breaks_the_effect_rules(string reason)
    {
        AuraTemplate burn = AuraTestData.Burn();
        AuraTemplate fortified = AuraTestData.Fortified();
        AuraTemplate stunned = AuraTestData.Stunned();
        var auras = new AuraCatalog([burn, fortified, stunned], static _ => null, NullLoggerFactory.Instance);
        var log = new TestLog();

        if (reason == "an effect aura reloaded helpful")
        {
            ResonanceEffect effect = Assert.Single(new ResonanceEffectCatalog([Row(Sound)], auras, log).Effects);
            var harness = new AuraHarness(logger: log);
            CharacterEntity wearer = harness.Player(1);
            Creature enemy = harness.Creature(2);

            // The same aura, reloaded as a heal over time: the row is sound, so only the fit at application refuses it.
            AuraTemplate reloaded = AuraTestData.Burn();
            reloaded.Kind = AuraKind.Helpful;
            reloaded.PeriodicKind = AuraPeriodicKind.Heal;
            harness.Use(reloaded);

            Assert.Equal(AuraApplyResult.Refused, harness.Auras.Apply(wearer, enemy, effect.Aura, effect.Source));
            Assert.Equal(0, enemy.Auras.Count);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("is Helpful"));
            return;
        }

        ItemThemeEffect subject = Row(Subject, themeId: 2);
        switch (reason)
        {
            case "its aura is not loaded": subject.AuraId = new AuraId(Missing); break;
            case "a helpful aura on an enemy": (subject.AuraId, subject.DamageType) = (fortified.Id, null); break;
            case "a harmful aura on the wearer": subject.Target = ResonanceTarget.Wearer; break;
            case "a hit target on a Struck trigger": subject.Trigger = ResonanceTrigger.Struck; break;
            case "the attacker on a Hit trigger": subject.Target = ResonanceTarget.Attacker; break;
            case "a hit target on a Kill trigger": subject.Trigger = ResonanceTrigger.Kill; break;
            case "a chain on the wearer":
                (subject.Target, subject.AuraId, subject.DamageType, subject.ChainTargets) =
                    (ResonanceTarget.Wearer, fortified.Id, null, 1);
                break;
            case "a damaging aura without a damage type": subject.DamageType = null; break;
            case "a damage type on a status": subject.AuraId = stunned.Id; break;
            case "an aura that stuns": (subject.AuraId, subject.DamageType) = (stunned.Id, null); break;
            case "a chance of 0": subject.ChancePct = 0f; break;
            case "a cooldown of 0": subject.CooldownMs = 0; break;
            case "a second row for one theme, threshold and trigger": subject.ThemeId = 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        }

        var catalog = new ResonanceEffectCatalog([subject, Row(Sound)], auras, log);

        Assert.Equal(Sound, Assert.Single(catalog.Effects).Id);
        Assert.Equal($"Resonance effect {Subject} is left out: {reason}", Assert.Single(log.Errors).Message);
    }

    /// <summary>A sound row: Burn on the hit target of a Hit at count 2, typed Fire.</summary>
    private static ItemThemeEffect Row(int id, int themeId = 1) => new()
    {
        Id = id,
        ThemeId = themeId,
        Count = 2,
        Trigger = ResonanceTrigger.Hit,
        ChancePct = 20f,
        CooldownMs = 2000,
        AuraId = AuraTestData.Burn().Id,
        Target = ResonanceTarget.HitTarget,
        DamageType = DamageType.Fire,
    };
}
