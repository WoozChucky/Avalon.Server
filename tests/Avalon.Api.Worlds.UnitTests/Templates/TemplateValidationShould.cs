using Avalon.Api.Worlds.Templates;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Templates;

/// <summary>
/// The field a world refusal is keyed by on a template edit (#1178). The world's rules answer a sentence, and an edit
/// answers it under the field the sentence is about; a refusal no branch recognises falls back to <c>template</c>, which
/// names no field an editor can fix. Each row is one of the offence model's refusals (AbilityRules, AuraRules and
/// AuraRules.SourceProblem), worded exactly as the rule words it.
/// </summary>
public class TemplateValidationShould
{
    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "ability", "DamageEffectiveness -1 is not a finite value of 0 or more", "damageEffectiveness" },
        { "ability", "SpellBasePerLevelPct -1 is not a finite value of 0 or more", "spellBasePerLevelPct" },
        { "ability", "unknown damage type 9", "damageType" },
        { "ability", "an Ally ability has no damage type", "damageType" },
        { "ability", "a class ability that deals damage names its damage type", "damageType" },
        { "ability", "Tags 200 names an unknown tag", "tags" },
        { "ability", "an ability is at most one of Attack, Spell and Heal", "tags" },
        { "ability", "DamageOverTime is an aura's tag, never an ability's", "tags" },
        { "ability", "only a Hostile ability is an Attack or a Spell, and only an Ally ability a Heal", "tags" },
        { "ability", "a Hostile ability that deals damage is an Attack or a Spell", "tags" },
        { "ability", "an Ally ability that heals is a Heal", "tags" },
        { "ability", "Projectile is the tag of a projectile, and only of one", "tags" },
        { "ability", "only an Attack is Melee", "tags" },
        { "ability", "a basic attack is paced by its weapon: its Cooldown is 0", "cooldown" },
        { "ability", "a class attack deals its weapon's physical damage: an element comes from its conversion or added damage", "damageType" },
        { "ability", "ConvertPct 150 is not a percentage from 0 to 100", "convertPct" },
        { "ability", "a conversion names an element to convert to", "convertToType" },
        { "ability", "only an Attack converts its weapon's physical damage", "convertPct" },
        { "ability", "only an Attack names a ConvertToType", "convertToType" },
        { "ability", "AddedMin 5 is above AddedMax 2", "addedMax" },
        { "ability", "unknown AddedType 9", "addedType" },
        { "ability", "added damage names its type", "addedType" },
        { "ability", "SpellBaseMin 20 is above SpellBaseMax 14", "spellBaseMax" },
        { "ability", "a Spell or a Heal that deals or heals needs a SpellBaseMax above 0", "spellBaseMax" },
        { "ability", "an Attack that deals damage needs a DamageEffectiveness above 0", "damageEffectiveness" },
        { "ability", "MaxTargets -1 is below 0", "maxTargets" },
        { "source", "a class ability that applies a damaging aura is an Attack or a Spell", "tags" },
        { "source", "an Ally ability that applies a healing aura is a Heal", "tags" },
        { "source", "a Spell or a Heal that deals or heals needs a SpellBaseMax above 0", "spellBaseMax" },
        { "aura", "PeriodicEffectiveness -1 is not a finite value of 0 or more", "periodicEffectiveness" },
        { "aura", "unknown modifier kind 9 on Armor", "modifiers" },
        { "aura", "Armor modifier -100 % would take the whole stat", "modifiers" },
        { "aura", "IncreasedFireDamage is already a percentage: it takes Flat or More, never Increased", "modifiers" },
        { "aura", "AttackSpeed is already a percentage: it takes Flat or More, never Increased", "modifiers" },
        { "aura", "Armor is modified twice", "modifiers" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Key_each_offence_refusal_by_the_field_it_is_about(string rule, string problem, string field)
    {
        string key = rule switch
        {
            "ability" => TemplateValidation.AbilityField(problem),
            "source" => TemplateValidation.AbilitySourceField(problem),
            _ => TemplateValidation.AuraField(problem),
        };

        Assert.Equal(field, key);
    }
}
