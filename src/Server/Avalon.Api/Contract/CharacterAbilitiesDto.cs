namespace Avalon.Api.Contract;

public sealed class CharacterAbilitiesDto
{
    public uint CharacterId { get; set; }
    public List<CharacterAbilityDto> Abilities { get; set; } = new();
}

public sealed class CharacterAbilityDto
{
    public uint AbilityId { get; set; }
    public CharacterAbilityTemplateDto? Template { get; set; }
}

public sealed class CharacterAbilityTemplateDto
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Cast time in milliseconds.</summary>
    public uint CastTime { get; set; }

    /// <summary>Cooldown in milliseconds.</summary>
    public uint Cooldown { get; set; }

    /// <summary>Cost in power points.</summary>
    public uint Cost { get; set; }

    public SpellRange Range { get; set; }
    public SpellEffect Effects { get; set; }
    public uint EffectValue { get; set; }
    public List<CharacterClass> AllowedClasses { get; set; } = [];
}
