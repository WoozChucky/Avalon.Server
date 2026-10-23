using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat;

/// <summary>
/// The offence maths (#1178): what an ability deals or heals before the hit's draws and the defender's mitigation. Pure:
/// the rolls come from <see cref="ICombatRandom" />, the modifiers from the attacker's <see cref="OffenceStats" />.
/// </summary>
/// <remarks>
/// An Attack rolls its weapon: main-hand range 1, range 2 when it has one, the weapon's own added damage by type
/// (Physical to Blight), the attacker's added damage to attacks by type, the ability's built-in added roll when it has
/// one, in that order (the contract, #1179). Then part of the physical damage, all of it whatever rolled it, is converted
/// (the ability's <c>ConvertPct</c> to its <c>ConvertToType</c>), then the built-in added damage joins, never converted. A
/// Spell rolls its spell base (grown by the caster's level), then the attacker's added damage to spells by type, then the
/// built-in added roll. Each part is then scaled on its own by <see cref="Scale" /> of its own type, so converted damage
/// takes the modifiers of the type it became and never its original type's. An Attack's every part, and a Spell's added
/// damage only, count for the ability's damage effectiveness, computed as <c>roll × effectiveness / 100</c> so whole
/// percentages stay exact.
/// </remarks>
public static class OffenceMath
{
    /// <summary>
    /// What a part of <paramref name="type" /> is multiplied by for an ability tagged <paramref name="tags" />: the
    /// increased sum <c>Damage + type + Attack (+ PhysicalAttack for physical) + Spell + Area + Projectile + Melee +
    /// DamageOverTime</c> over the tags present, as <c>max(0, 1 + sum / 100)</c>, times the more product of the same keys.
    /// </summary>
    public static double Scale(DamageType type, AbilityTags tags, in OffenceStats o) =>
        PartScale(type, tags, o, TermsOf(tags, o));

    /// <summary>
    /// <see cref="Scale(DamageType, AbilityTags, in OffenceStats)" /> from the <paramref name="terms" /> every part of the
    /// ability shares, adding only the part's own type key (and PhysicalAttack for an Attack's physical part), so a base
    /// with several parts sums the tag keys once.
    /// </summary>
    private static double PartScale(DamageType type, AbilityTags tags, in OffenceStats o, in ScaleTerms terms)
    {
        OffenceStat typeKey = KeyOf(type);
        double increased = terms.Increased + o.Increased(typeKey);
        double more = terms.More * o.More(typeKey);
        if (type == DamageType.Physical && (tags & AbilityTags.Attack) != 0)
            Add(o, OffenceStat.PhysicalAttack, ref increased, ref more);

        return Math.Max(0d, 1d + (increased / 100d)) * more;
    }

    /// <summary>The increased sum and more product of Damage and of each tag key the ability has, whatever the part's type.</summary>
    private static ScaleTerms TermsOf(AbilityTags tags, in OffenceStats o)
    {
        double increased = o.Increased(OffenceStat.Damage);
        double more = o.More(OffenceStat.Damage);
        if ((tags & AbilityTags.Attack) != 0) Add(o, OffenceStat.Attack, ref increased, ref more);
        if ((tags & AbilityTags.Spell) != 0) Add(o, OffenceStat.Spell, ref increased, ref more);
        if ((tags & AbilityTags.Area) != 0) Add(o, OffenceStat.Area, ref increased, ref more);
        if ((tags & AbilityTags.Projectile) != 0) Add(o, OffenceStat.Projectile, ref increased, ref more);
        if ((tags & AbilityTags.Melee) != 0) Add(o, OffenceStat.Melee, ref increased, ref more);
        if ((tags & AbilityTags.DamageOverTime) != 0) Add(o, OffenceStat.DamageOverTime, ref increased, ref more);
        return new ScaleTerms(increased, more);
    }

    private readonly record struct ScaleTerms(double Increased, double More);

    /// <summary>What a heal is multiplied by: <c>max(0, 1 + Healing / 100) × More(Healing)</c>; nothing else boosts a heal.</summary>
    public static double HealScale(in OffenceStats o) =>
        Math.Max(0d, 1d + (o.Increased(OffenceStat.Healing) / 100d)) * o.More(OffenceStat.Healing);

    /// <summary>
    /// An Attack's parts: its weapon's ranges (each typed by the weapon, else the hit's type), the weapon's added damage,
    /// the attacker's added damage to attacks, the conversion of the physical part, the built-in added damage, each part
    /// times its scale and the ability's damage effectiveness.
    /// </summary>
    public static DamageParts AttackBase(in AttackerCombat a, AbilityMetadata ability, ICombatRandom rng)
    {
        DamageType hitType = HitResolver.TypeOf(ability.DamageType, a);
        WeaponDamage weapon = a.Weapon;
        OffenceStats o = a.Offence;
        DamageParts parts = default;

        if (weapon.Max1 > 0) AddRoll(ref parts, weapon.Type1 ?? hitType, weapon.Min1, weapon.Max1, rng);
        if (weapon.Max2 > 0) AddRoll(ref parts, weapon.Type2 ?? hitType, weapon.Min2, weapon.Max2, rng);
        AddRolls(ref parts, weapon.Added, rng);
        AddRolls(ref parts, o.AddedToAttacks, rng);
        long builtIn = ability.AddedMax > 0 ? Roll(ability.AddedMin, ability.AddedMax, rng) : 0;

        Convert(ref parts, ability);
        if (builtIn > 0) Add(ref parts, ability.AddedType ?? hitType, builtIn);

        AbilityTags tags = ability.Tags;
        ScaleTerms terms = TermsOf(tags, o);
        double effectiveness = NonNegative(ability.DamageEffectiveness);
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            var type = (DamageType)i;
            double part = parts.Get(type);
            if (part == 0d) continue;

            parts.Set(type, part * effectiveness / 100d * PartScale(type, tags, o, terms));
        }

        return parts;
    }

    /// <summary>
    /// A Spell's parts: its spell base grown by the caster's level, in the hit's type, times its scale; then the
    /// attacker's added damage to spells by type and the built-in added damage, each times its scale and the ability's
    /// damage effectiveness. No conversion.
    /// </summary>
    public static DamageParts SpellBase(in AttackerCombat a, AbilityMetadata ability, ICombatRandom rng)
    {
        DamageType hitType = HitResolver.TypeOf(ability.DamageType, a);
        OffenceStats o = a.Offence;
        double spell = SpellRoll(a.Level, ability, rng);
        DamageParts added = default;
        AddRolls(ref added, o.AddedToSpells, rng);
        long builtIn = ability.AddedMax > 0 ? Roll(ability.AddedMin, ability.AddedMax, rng) : 0;

        AbilityTags tags = ability.Tags;
        ScaleTerms terms = TermsOf(tags, o);
        double effectiveness = NonNegative(ability.DamageEffectiveness);
        DamageParts parts = default;
        if (spell > 0d) Add(ref parts, hitType, spell * PartScale(hitType, tags, o, terms));
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            var type = (DamageType)i;
            double roll = added.Get(type);
            if (roll > 0d) Add(ref parts, type, roll * effectiveness / 100d * PartScale(type, tags, o, terms));
        }

        if (builtIn > 0)
        {
            DamageType builtInType = ability.AddedType ?? hitType;
            Add(ref parts, builtInType, builtIn * effectiveness / 100d * PartScale(builtInType, tags, o, terms));
        }

        return parts;
    }

    /// <summary>A Heal's amount: its spell base grown by the caster's level, times <see cref="HealScale" />.</summary>
    public static double HealBase(in AttackerCombat a, AbilityMetadata ability, ICombatRandom rng)
    {
        double heal = SpellRoll(a.Level, ability, rng);
        return heal > 0d ? heal * HealScale(a.Offence) : 0d;
    }

    /// <summary>
    /// An ability's direct damage: <see cref="AttackBase" /> for an Attack, <see cref="SpellBase" /> for a Spell. Only a
    /// damaging ability's: a heal goes through <see cref="HealBase" />.
    /// </summary>
    public static DamageParts DirectBase(in AttackerCombat a, AbilityMetadata ability, ICombatRandom rng) =>
        (ability.Tags & AbilityTags.Attack) != 0 ? AttackBase(a, ability, rng) : SpellBase(a, ability, rng);

    /// <summary>
    /// The whole amount a periodic aura deals or heals over its ticks, rolled once, at application:
    /// <c>(PeriodicBase + source roll × PeriodicEffectiveness / 100) × scale</c>. The source roll is the caster's weapon
    /// (both ranges) for an Attack, else the source's spell base grown by the caster's level, never its conversion or
    /// built-in added damage; it is drawn only when the effectiveness is above 0. A damage aura scales by
    /// <see cref="Scale" /> of <paramref name="type" /> with its source's tags plus DamageOverTime, a heal aura by
    /// <see cref="HealScale" />. With no <paramref name="source" />, PeriodicBase alone.
    /// </summary>
    public static double PeriodicTotal(AuraTemplate aura, in AttackerCombat caster, AbilityMetadata? source,
        DamageType type, ICombatRandom rng)
    {
        double periodicBase = NonNegative(aura.PeriodicBase);
        if (source is null) return periodicBase;

        double effectiveness = NonNegative(aura.PeriodicEffectiveness);
        double sourceRoll = effectiveness > 0d ? SourceRoll(caster, source, rng) : 0d;
        double total = periodicBase + (sourceRoll * effectiveness / 100d);

        OffenceStats o = caster.Offence;
        return aura.PeriodicKind == AuraPeriodicKind.Heal
            ? total * HealScale(o)
            : total * Scale(type, source.Tags | AbilityTags.DamageOverTime, o);
    }

    private static double SourceRoll(in AttackerCombat caster, AbilityMetadata source, ICombatRandom rng)
    {
        if ((source.Tags & AbilityTags.Attack) == 0) return SpellRoll(caster.Level, source, rng);

        WeaponDamage weapon = caster.Weapon;
        long roll = weapon.Max1 > 0 ? Roll(weapon.Min1, weapon.Max1, rng) : 0;
        if (weapon.Max2 > 0) roll += Roll(weapon.Min2, weapon.Max2, rng);
        return roll;
    }

    /// <summary>
    /// The spell base rolled inclusive, times <c>1 + SpellBasePerLevelPct / 100 × (level − 1)</c>, computed as
    /// <c>roll × (100 + pct × (level − 1)) / 100</c>; nothing is drawn for a base of 0.
    /// </summary>
    private static double SpellRoll(uint level, AbilityMetadata ability, ICombatRandom rng)
    {
        if (ability.SpellBaseMax == 0) return 0d;

        long roll = Roll(ability.SpellBaseMin, ability.SpellBaseMax, rng);
        double growthPct = 100d + (NonNegative(ability.SpellBasePerLevelPct) * (Math.Max(level, 1u) - 1u));
        return roll * growthPct / 100d;
    }

    /// <summary>The physical part's <c>ConvertPct</c> (held to 0..100) moved to <c>ConvertToType</c>.</summary>
    private static void Convert(ref DamageParts parts, AbilityMetadata ability)
    {
        if (ability.ConvertToType is not { } to || to == DamageType.Physical) return;

        double pct = Math.Min(NonNegative(ability.ConvertPct), 100d);
        double physical = parts.Get(DamageType.Physical);
        if (pct == 0d || physical == 0d) return;

        double converted = physical * pct / 100d;
        parts.Set(DamageType.Physical, physical - converted);
        Add(ref parts, to, converted);
    }

    private static void AddRoll(ref DamageParts parts, DamageType type, uint min, uint max, ICombatRandom rng) =>
        Add(ref parts, type, Roll(min, max, rng));

    /// <summary>Each type's range of <paramref name="ranges" /> that adds anything, rolled in type order, Physical to Blight.</summary>
    private static void AddRolls(ref DamageParts parts, in DamageRanges ranges, ICombatRandom rng)
    {
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            var type = (DamageType)i;
            uint max = ranges.Max(type);
            if (max > 0) AddRoll(ref parts, type, ranges.Min(type), max, rng);
        }
    }

    private static long Roll(uint min, uint max, ICombatRandom rng) => rng.NextInt64(Math.Min(min, max), max);

    private static void Add(ref DamageParts parts, DamageType type, double amount) =>
        parts.Set(type, parts.Get(type) + amount);

    private static void Add(in OffenceStats o, OffenceStat key, ref double increased, ref double more)
    {
        increased += o.Increased(key);
        more *= o.More(key);
    }

    /// <summary>A damage type's own key: Physical 1 to Blight 5, one past the type's number.</summary>
    private static OffenceStat KeyOf(DamageType type)
    {
        if ((uint)type >= DamageTypes.Count) ThrowOutOfRange(type);
        return (OffenceStat)((int)type + 1);
    }

    private static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0d;

    // Kept out of line so KeyOf stays small enough to inline (an inline throw boxes the enum argument).
    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOutOfRange(DamageType type) =>
        throw new ArgumentOutOfRangeException(nameof(type), type, null);
}
