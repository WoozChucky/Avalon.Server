using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>The checks on a creature template, shared by the world server's Creatures reload and the balance simulator.</summary>
public static class CreatureTemplateRules
{
    /// <summary>The shortest swing interval a template may have, in seconds (#627); a database check says the same.</summary>
    public const float MinBaseAttackTime = 0.5f;

    /// <summary>
    /// #627: refuses the whole area, naming the template, when one swings more often than every half second
    /// or at an interval that is not finite, so a bad row leaves the previous generation live.
    /// </summary>
    /// <exception cref="InvalidDataException">A template is out of range; the message names it.</exception>
    public static void Validate(IEnumerable<CreatureTemplate> templates)
    {
        foreach (CreatureTemplate t in templates)
        {
            if (!float.IsFinite(t.BaseAttackTime) || t.BaseAttackTime < MinBaseAttackTime)
            {
                throw new InvalidDataException(
                    $"CreatureTemplate {t.Id.Value}: BaseAttackTime must be finite and {MinBaseAttackTime} s or more, not {t.BaseAttackTime}");
            }
        }
    }
}
