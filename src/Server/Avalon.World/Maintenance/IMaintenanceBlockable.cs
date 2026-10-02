namespace Avalon.World.Maintenance;

/// <summary>
/// Stops a connection's player packets at the maintenance cutoff. Lives in Avalon.World, deliberately outside
/// Avalon.World.Public, so no mod can silence a player's connection.
/// </summary>
public interface IMaintenanceBlockable
{
    /// <summary>Drops every queued and later player packet. Called on the tick.</summary>
    void BlockForMaintenance();
}
