namespace Avalon.Api.Contract;

/// <summary>
/// The worlds the caller may read, for tooltips and for the public realm board, and the one a link without a world
/// reads. Anonymous and cacheable by no one: it carries no host, port, version, exact count or limit.
/// </summary>
public sealed class PublicWorldsDto
{
    public ushort? DefaultWorldId { get; set; }
    public List<PublicWorldDto> Worlds { get; set; } = [];
}

public sealed class PublicWorldDto
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
    public WorldType Type { get; set; }
    public WorldStatus Status { get; set; }

    /// <summary>A band, never a head count; null when the world is offline, in maintenance or has no capacity to compare with.</summary>
    public PublicPopulation? Population { get; set; }

    public int QueueLength { get; set; }

    /// <summary>When scheduled maintenance cuts the world off; null when none is scheduled.</summary>
    public DateTime? MaintenanceDeadlineUtc { get; set; }

    public string? Lore { get; set; }
}
