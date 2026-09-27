using System.Diagnostics.CodeAnalysis;
using Avalon.Domain.Auth;

namespace Avalon.Api.Worlds;

/// <summary>The worlds this api is configured for (Database:Worlds), with their status (#523).</summary>
public interface IWorldDatabases
{
    /// <summary>Every configured world, in id order.</summary>
    IReadOnlyList<ConfiguredWorld> All { get; }

    bool TryGet(WorldId world, [NotNullWhen(true)] out ConfiguredWorld? configured);

    /// <summary>
    /// The world is configured and its databases migrated at startup. The one test of "this api can
    /// serve that world"; false for a world it was not given.
    /// </summary>
    bool IsAvailable(WorldId world);
}
