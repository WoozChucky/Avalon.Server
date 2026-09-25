using Avalon.Common.Accounts;

namespace Avalon.World;

/// <summary>
/// The only way to set a connection's access level. Lives in Avalon.World, deliberately outside
/// Avalon.World.Public, so the modding API exposes the level as read-only.
/// </summary>
public interface IAccessLevelAssignable
{
    void AssignAccessLevel(AccountAccessLevel level);
}
