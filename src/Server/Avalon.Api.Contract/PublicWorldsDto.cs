namespace Avalon.Api.Contract;

/// <summary>The worlds the caller may read tooltips from, and the one a link without a world reads.</summary>
public sealed class PublicWorldsDto
{
    public ushort? DefaultWorldId { get; set; }
    public List<PublicWorldDto> Worlds { get; set; } = [];
}

public sealed class PublicWorldDto
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
}
