using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using static Avalon.Database.World.Seeding.ItemModifierSeed;

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The seeded Resonance effects (#1180 part A2): what each of the eight themes does at the counts 2, 3 and 4, and the 29
/// auras those effects apply. The model's HasData, the catalog exports and the tests read it from here.
/// <list type="bullet">
/// <item>Ids are literal surrogates, in the design's table order: effects 1 to 29 and their auras 16 to 44. A row added
/// later takes the next id above the highest ever used.</item>
/// <item>An aura's icon is its family's name in lower case, so every tier of a family shares one; no aura names a
/// script, and every modifier is Flat.</item>
/// <item>The slows (Chilled) break as Crippled does, 0.5 % a second and never on damage; the root (Rimebound) as Bound
/// and Frozen do. Rimebound is not named Frozen: aura 14 already is, and two auras of one name would read as one.</item>
/// <item>The one-tick bursts (Jolt, Barbs) last 200 ms with a 200 ms tick, so each lands once, 0.2 s after its trigger.
/// They are Independent, as every damaging effect aura is, so two wearers' copies on one unit never renew each
/// other.</item>
/// <item>A row whose aura deals damage names its damage type (Ember's Fire, Storm's Lightning, Thorn's Physical); no
/// other row has one.</item>
/// </list>
/// </summary>
public static class ResonanceSeed
{
    private const float SlowBreakPerSecond = 0.5f;
    private const float RootBreakOnDamage = 25f;
    private const float RootBreakPerSecond = 0.5f;

    /// <summary>The 29 effect auras, ids 16 to 44, without their modifiers (<see cref="AuraModifiers" />).</summary>
    public static AuraTemplate[] Auras() =>
    [
        Ticking(16, "Burning I", "burning", AuraKind.Harmful, 4000, 1000, AuraPeriodicKind.Damage, 16f, AuraStacking.Independent),
        Ticking(17, "Burning II", "burning", AuraKind.Harmful, 4000, 1000, AuraPeriodicKind.Damage, 22f, AuraStacking.Independent),
        Ticking(18, "Burning III", "burning", AuraKind.Harmful, 4000, 1000, AuraPeriodicKind.Damage, 28f, AuraStacking.Independent),
        Slow(19, "Chilled I"),
        Slow(20, "Chilled II"),
        Slow(21, "Chilled III"),
        Aura(22, "Rimebound", "rimebound", AuraKind.Harmful, 1000, control: CrowdControl.Root,
            breakOnDamage: RootBreakOnDamage, breakPerSecond: RootBreakPerSecond),
        Ticking(23, "Jolt I", "jolt", AuraKind.Harmful, 200, 200, AuraPeriodicKind.Damage, 14f, AuraStacking.Independent),
        Ticking(24, "Jolt II", "jolt", AuraKind.Harmful, 200, 200, AuraPeriodicKind.Damage, 18f, AuraStacking.Independent),
        Ticking(25, "Jolt III", "jolt", AuraKind.Harmful, 200, 200, AuraPeriodicKind.Damage, 22f, AuraStacking.Independent),
        Ticking(26, "Regrowth I", "regrowth", AuraKind.Helpful, 4000, 1000, AuraPeriodicKind.Heal, 16f, AuraStacking.Refresh),
        Ticking(27, "Regrowth II", "regrowth", AuraKind.Helpful, 4000, 1000, AuraPeriodicKind.Heal, 24f, AuraStacking.Refresh),
        Ticking(28, "Regrowth III", "regrowth", AuraKind.Helpful, 4000, 1000, AuraPeriodicKind.Heal, 32f, AuraStacking.Refresh),
        Ticking(29, "Rootsurge", "rootsurge", AuraKind.Helpful, 3000, 1000, AuraPeriodicKind.Heal, 48f, AuraStacking.Refresh),
        Aura(30, "Guard I", "guard", AuraKind.Helpful, 4000),
        Aura(31, "Guard II", "guard", AuraKind.Helpful, 4000),
        Aura(32, "Guard III", "guard", AuraKind.Helpful, 4000),
        Ticking(33, "Barbs I", "barbs", AuraKind.Harmful, 200, 200, AuraPeriodicKind.Damage, 5f, AuraStacking.Independent),
        Ticking(34, "Barbs II", "barbs", AuraKind.Harmful, 200, 200, AuraPeriodicKind.Damage, 8f, AuraStacking.Independent),
        Ticking(35, "Barbs III", "barbs", AuraKind.Harmful, 200, 200, AuraPeriodicKind.Damage, 10f, AuraStacking.Independent),
        Ticking(36, "Bleeding", "bleeding", AuraKind.Harmful, 4000, 1000, AuraPeriodicKind.Damage, 20f, AuraStacking.Independent),
        Aura(37, "Tailwind I", "tailwind", AuraKind.Helpful, 4000),
        Aura(38, "Tailwind II", "tailwind", AuraKind.Helpful, 4000),
        Aura(39, "Tailwind III", "tailwind", AuraKind.Helpful, 5000),
        Aura(40, "Slipstream", "slipstream", AuraKind.Helpful, 2000),
        Aura(41, "Veiled I", "veiled", AuraKind.Helpful, 3000),
        Aura(42, "Veiled II", "veiled", AuraKind.Helpful, 3000),
        Aura(43, "Veiled III", "veiled", AuraKind.Helpful, 4000),
        Aura(44, "Lucid", "lucid", AuraKind.Helpful, 4000),
    ];

    /// <summary>The effect auras' stat modifiers, every one Flat: 26 rows.</summary>
    public static AuraStatModifier[] AuraModifiers() =>
    [
        // Chilled I-III: slower movement, and from II slower attacks.
        Flat(19, AuraStat.MovementSpeed, -15f),
        Flat(20, AuraStat.MovementSpeed, -20f), Flat(20, AuraStat.AttackSpeed, -8f),
        Flat(21, AuraStat.MovementSpeed, -25f), Flat(21, AuraStat.AttackSpeed, -12f),

        // Guard I-III: armour and block, and at III a ward.
        Flat(30, AuraStat.Armor, 15f), Flat(30, AuraStat.BlockPct, 2f),
        Flat(31, AuraStat.Armor, 25f), Flat(31, AuraStat.BlockPct, 4f),
        Flat(32, AuraStat.Armor, 35f), Flat(32, AuraStat.BlockPct, 5f), Flat(32, AuraStat.MaxWard, 25f),

        // Tailwind I-III: movement, and from II attack and cast speed; Slipstream: movement.
        Flat(37, AuraStat.MovementSpeed, 10f),
        Flat(38, AuraStat.MovementSpeed, 12f), Flat(38, AuraStat.AttackSpeed, 5f), Flat(38, AuraStat.CastSpeed, 5f),
        Flat(39, AuraStat.MovementSpeed, 15f), Flat(39, AuraStat.AttackSpeed, 8f), Flat(39, AuraStat.CastSpeed, 8f),
        Flat(40, AuraStat.MovementSpeed, 20f),

        // Veiled I-III: evasion, and from II movement; Lucid: cast speed.
        Flat(41, AuraStat.Evasion, 60f),
        Flat(42, AuraStat.Evasion, 100f), Flat(42, AuraStat.MovementSpeed, 10f),
        Flat(43, AuraStat.Evasion, 150f), Flat(43, AuraStat.MovementSpeed, 15f),
        Flat(44, AuraStat.CastSpeed, 10f),
    ];

    /// <summary>The 29 effects, ids 1 to 29: each theme's rows at the counts 2, 3 and 4, then its count-4 extra.</summary>
    public static ItemThemeEffect[] Effects() =>
    [
        // Ember: burns what it hits.
        Effect(1, Ember, 2, ResonanceTrigger.Hit, 20f, 2000, 16, ResonanceTarget.HitTarget, damageType: DamageType.Fire),
        Effect(2, Ember, 3, ResonanceTrigger.Hit, 25f, 2000, 17, ResonanceTarget.HitTarget, damageType: DamageType.Fire),
        Effect(3, Ember, 4, ResonanceTrigger.Hit, 30f, 2000, 18, ResonanceTarget.HitTarget, damageType: DamageType.Fire),

        // Rime: chills what it hits, and at 4 a crit roots it.
        Effect(4, Rime, 2, ResonanceTrigger.Hit, 20f, 3000, 19, ResonanceTarget.HitTarget),
        Effect(5, Rime, 3, ResonanceTrigger.Hit, 25f, 3000, 20, ResonanceTarget.HitTarget),
        Effect(6, Rime, 4, ResonanceTrigger.Hit, 30f, 3000, 21, ResonanceTarget.HitTarget),
        Effect(7, Rime, 4, ResonanceTrigger.Crit, 25f, 10000, 22, ResonanceTarget.HitTarget),

        // Storm: a crit jolts its target, and from 3 the creatures nearest it.
        Effect(8, Storm, 2, ResonanceTrigger.Crit, 100f, 2000, 23, ResonanceTarget.HitTarget, damageType: DamageType.Lightning),
        Effect(9, Storm, 3, ResonanceTrigger.Crit, 100f, 2000, 24, ResonanceTarget.HitTarget, chainTargets: 1,
            damageType: DamageType.Lightning),
        Effect(10, Storm, 4, ResonanceTrigger.Crit, 100f, 2000, 25, ResonanceTarget.HitTarget, chainTargets: 2,
            damageType: DamageType.Lightning),

        // Wild: a kill heals the wearer over time, and at 4 a hit taken at low life heals more.
        Effect(11, Wild, 2, ResonanceTrigger.Kill, 100f, 4000, 26, ResonanceTarget.Wearer),
        Effect(12, Wild, 3, ResonanceTrigger.Kill, 100f, 4000, 27, ResonanceTarget.Wearer),
        Effect(13, Wild, 4, ResonanceTrigger.Kill, 100f, 4000, 28, ResonanceTarget.Wearer),
        Effect(14, Wild, 4, ResonanceTrigger.Struck, 100f, 20000, 29, ResonanceTarget.Wearer, atOrBelowLifePct: 35f),

        // Iron: a hit taken may raise the wearer's guard.
        Effect(15, Iron, 2, ResonanceTrigger.Struck, 20f, 6000, 30, ResonanceTarget.Wearer),
        Effect(16, Iron, 3, ResonanceTrigger.Struck, 25f, 6000, 31, ResonanceTarget.Wearer),
        Effect(17, Iron, 4, ResonanceTrigger.Struck, 30f, 6000, 32, ResonanceTarget.Wearer),

        // Thorn: a hit taken barbs the attacker, and at 4 a crit makes its target bleed.
        Effect(18, Thorn, 2, ResonanceTrigger.Struck, 100f, 2000, 33, ResonanceTarget.Attacker, damageType: DamageType.Physical),
        Effect(19, Thorn, 3, ResonanceTrigger.Struck, 100f, 2000, 34, ResonanceTarget.Attacker, damageType: DamageType.Physical),
        Effect(20, Thorn, 4, ResonanceTrigger.Struck, 100f, 2000, 35, ResonanceTarget.Attacker, damageType: DamageType.Physical),
        Effect(21, Thorn, 4, ResonanceTrigger.Crit, 100f, 4000, 36, ResonanceTarget.HitTarget, damageType: DamageType.Physical),

        // Gale: a kill speeds the wearer up, and at 4 an evaded hit does too.
        Effect(22, Gale, 2, ResonanceTrigger.Kill, 100f, 5000, 37, ResonanceTarget.Wearer),
        Effect(23, Gale, 3, ResonanceTrigger.Kill, 100f, 5000, 38, ResonanceTarget.Wearer),
        Effect(24, Gale, 4, ResonanceTrigger.Kill, 100f, 5000, 39, ResonanceTarget.Wearer),
        Effect(25, Gale, 4, ResonanceTrigger.Evaded, 100f, 8000, 40, ResonanceTarget.Wearer),

        // Veil: a broken ward veils the wearer, and at 4 a crit clears its mind.
        Effect(26, Veil, 2, ResonanceTrigger.WardBroken, 100f, 12000, 41, ResonanceTarget.Wearer),
        Effect(27, Veil, 3, ResonanceTrigger.WardBroken, 100f, 12000, 42, ResonanceTarget.Wearer),
        Effect(28, Veil, 4, ResonanceTrigger.WardBroken, 100f, 12000, 43, ResonanceTarget.Wearer),
        Effect(29, Veil, 4, ResonanceTrigger.Crit, 100f, 8000, 44, ResonanceTarget.Wearer),
    ];

    private static AuraTemplate Ticking(uint id, string name, string icon, AuraKind kind, uint durationMs, uint tickIntervalMs,
        AuraPeriodicKind periodic, float periodicBase, AuraStacking stacking) =>
        Aura(id, name, icon, kind, durationMs, stacking, tickIntervalMs, periodic, periodicBase);

    private static AuraTemplate Slow(uint id, string name) =>
        Aura(id, name, "chilled", AuraKind.Harmful, 3000, breakPerSecond: SlowBreakPerSecond);

    private static AuraTemplate Aura(uint id, string name, string icon, AuraKind kind, uint durationMs,
        AuraStacking stacking = AuraStacking.Refresh, uint tickIntervalMs = 0, AuraPeriodicKind periodic = AuraPeriodicKind.None,
        float periodicBase = 0f, CrowdControl control = CrowdControl.None, float breakOnDamage = 0f,
        float breakPerSecond = 0f) => new()
        {
            Id = id,
            Name = name,
            Icon = icon,
            Kind = kind,
            DurationMs = durationMs,
            TickIntervalMs = tickIntervalMs,
            PeriodicKind = periodic,
            PeriodicBase = periodicBase,
            Stacking = stacking,
            MaxStacks = 1,
            Control = control,
            BreakOnDamagePct = breakOnDamage,
            BreakPerSecondPct = breakPerSecond,
        };

    private static AuraStatModifier Flat(uint aura, AuraStat stat, float value) =>
        new() { AuraId = aura, Stat = stat, Kind = AuraModifierKind.Flat, Value = value };

    private static ItemThemeEffect Effect(int id, int theme, int count, ResonanceTrigger trigger, float chancePct,
        uint cooldownMs, uint aura, ResonanceTarget target, int chainTargets = 0, float? atOrBelowLifePct = null,
        DamageType? damageType = null) => new()
        {
            Id = id,
            ThemeId = theme,
            Count = count,
            Trigger = trigger,
            ChancePct = chancePct,
            CooldownMs = cooldownMs,
            AuraId = aura,
            Target = target,
            ChainTargets = chainTargets,
            AtOrBelowLifePct = atOrBelowLifePct,
            DamageType = damageType,
        };
}
