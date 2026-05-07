using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

// Bridge attribute: the C# class is renamed in this commit, but the underlying DB table
// is still "SpellTemplates" until A8 performs the schema rename. Without an explicit
// [Table] attribute, EF derives the table name from the DbSet property name; keeping it
// pinned here makes the rename safe regardless of the DbSet name.
[Table("SpellTemplates")]
public class AbilityTemplate : IDbEntity<AbilityId>
{
    [Key]
    public AbilityId Id { get; set; }

    public string Name { get; set; }

    public uint CastTime { get; set; } // in milliseconds

    public uint Cooldown { get; set; } // in milliseconds

    public uint Cost { get; set; } // in power points

    public string SpellScript { get; set; }

    public SpellRange Range { get; set; } // in meters

    public SpellEffect Effects { get; set; }

    public uint EffectValue { get; set; }

    public List<CharacterClass> AllowedClasses { get; set; } = []; // Default to no classes
}
