namespace Avalon.Api.Contract;

/// <summary>
/// The talent nodes a character holds as its world last saved them (#1181), with the points it has earned and spent.
/// Nothing here is stored: the points are worked out from the saved rows, as the world works them out.
/// </summary>
public sealed class CharacterTalentsDto
{
    public uint CharacterId { get; set; }

    /// <summary>The held node ids, ascending; the class start, which is never allocated, is not among them.</summary>
    public List<int> NodeIds { get; set; } = [];

    /// <summary>One per level, plus what each quest it has turned in pays.</summary>
    public int PointsEarned { get; set; }

    /// <summary>One per held node.</summary>
    public int PointsSpent { get; set; }
}
