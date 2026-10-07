using Avalon.World.Public.Enums;

namespace Avalon.Api.Contract;

public sealed class AbilityTemplateDto
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Cast time in milliseconds.</summary>
    public uint CastTime { get; set; }

    /// <summary>Cooldown in milliseconds.</summary>
    public uint Cooldown { get; set; }

    /// <summary>Cost in power points.</summary>
    public uint Cost { get; set; }

    /// <summary>The pool <see cref="Cost"/> is spent from; None when the ability costs nothing.</summary>
    public PowerType CostPowerType { get; set; }

    public string ScriptName { get; set; } = "";
    public SpellRange Range { get; set; }
    public SpellEffect Effects { get; set; }
    public uint EffectValue { get; set; }
    public List<CharacterClass> AllowedClasses { get; set; } = [];

    /// <summary>The aura each unit the ability affects receives, or null (auras).</summary>
    public uint? AuraId { get; set; }

    /// <summary>
    /// The row's version: a lowercase hex SHA-256 of its stored values, also sent as the ETag on a single read.
    /// An edit sends it back as If-Match.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>Whether this world's templates can be edited; false on a read-only world.</summary>
    public bool Editable { get; set; }
}
