using Avalon.Common.GameAuth;

namespace Avalon.World.Entities;

public partial class CharacterEntity
{
    public GameplayWriteAuthority? GameplayAuthority { get; private set; }

    // Bound when this entity is loaded. A replacement connection needs a new entity.
    public void BindGameplayAuthority(GameplayWriteAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (Data is null || Data.AccountId != authority.AccountId || authority.GameSessionId == System.Guid.Empty || authority.FencingToken <= 0)
            throw new InvalidOperationException("The admission authority does not own this entity.");
        if (GameplayAuthority is { } existing)
        {
            if (existing.GameSessionId != authority.GameSessionId || existing.FencingToken != authority.FencingToken || existing.AccountId != authority.AccountId)
                throw new InvalidOperationException("An entity cannot move between gameplay sessions.");
            return;
        }
        GameplayAuthority = authority;
    }
}
