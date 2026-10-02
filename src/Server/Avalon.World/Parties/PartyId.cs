using Avalon.Common;

namespace Avalon.World.Parties;

/// <summary>A party, unique for one run of the world server (parties are not persisted). Never reused. World-side.</summary>
public sealed class PartyId : ValueObject<uint>, IHideObjectMembers
{
    public PartyId(uint value) : base(value)
    {
    }
}
