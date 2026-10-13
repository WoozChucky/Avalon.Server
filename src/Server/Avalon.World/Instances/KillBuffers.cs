using Avalon.Common;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;

namespace Avalon.World.Instances;

/// <summary>
/// The lists one kill fills and reads before it returns (#890), kept by its <see cref="MapInstance" /> so that a kill
/// allocates none of them. An instance's kills run one at a time on its tick, so one set serves them all; it belongs to
/// the instance and never to a static, since instances may come to tick on separate threads (#639).
/// <see cref="Clear" /> empties it when the kill is done, so it keeps no character alive between kills.
/// </summary>
internal sealed class KillBuffers
{
    /// <summary>Who shares the kill, in the party's join order.</summary>
    public List<ICharacter> Eligible { get; } = [];

    /// <summary>The key each party member is looked up by among the instance's characters; never stored anywhere.</summary>
    public ObjectGuid MemberKey { get; } = new();

    /// <summary>Each counted member's share of the kill's experience, in the order of <see cref="Eligible" />.</summary>
    public List<ExperienceShare> Shares { get; } = [];

    public void Clear()
    {
        Eligible.Clear();
        Shares.Clear();
    }
}
