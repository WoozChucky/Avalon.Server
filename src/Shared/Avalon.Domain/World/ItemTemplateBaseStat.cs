using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One base stat a template's copies roll (#1179): the authored range, both ends included. A template carries exactly
/// the stats its kind asks for (<see cref="ItemBaseStatRules" />), one row each, keyed by (TemplateId, Stat). An edit
/// reaches new copies only: a copy keeps the value it rolled.
/// </summary>
public class ItemTemplateBaseStat
{
    public ItemTemplateId TemplateId { get; set; } = null!;

    public ItemBaseStat Stat { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }
}
