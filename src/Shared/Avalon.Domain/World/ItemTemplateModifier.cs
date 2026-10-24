using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// A modifier every copy of a template rolls when it is made (#1179), in <see cref="Index" /> order: a base's implicits,
/// or a unique's own modifier. A unique template links exactly one Unique modifier, a common template only Implicit
/// ones, and no template a prefix or a suffix.
/// </summary>
public class ItemTemplateModifier
{
    public ItemTemplateId TemplateId { get; set; } = null!;

    public byte Index { get; set; }

    public int ModifierId { get; set; }
}
