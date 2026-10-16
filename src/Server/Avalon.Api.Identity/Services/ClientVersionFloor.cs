using Avalon.Common.Utils;
using Avalon.Configuration;

namespace Avalon.Api.Identity.Services;

/// <summary>
/// The oldest client game sign-in and world allocation accept, <see cref="GameWorkloadConfiguration.MinClientVersion"/>
/// (#949): a client stating a version at or above it is accepted, so a client can ship before the services as long as it
/// is at or above the floor, and raising the floor is a deploy.
/// </summary>
public static class ClientVersionFloor
{
    public const string Setting = "Application:GameWorkloads:MinClientVersion";

    /// <summary>What startup says of a value it refuses.</summary>
    public const string Refusal = Setting + " must be a version of three whole numbers, major.minor.patch (major and " +
        "minor at most 255, patch at most 65535), e.g. 1.2.3.";

    /// <summary>Whether <paramref name="floor"/> is a usable setting: <see cref="TryParse"/> reads it.</summary>
    public static bool IsValid(string? floor) => TryParse(floor, out _);

    /// <summary>
    /// Whether a client stating <paramref name="clientVersion"/> is accepted: one that does not parse is refused, and
    /// every client is refused while the floor does not parse (startup refuses that).
    /// </summary>
    public static bool Admits(this GameWorkloadConfiguration workloads, string? clientVersion) =>
        TryParse(workloads.MinClientVersion, out uint floor) && TryParse(clientVersion, out uint version) && version >= floor;

    /// <summary>
    /// Reads <c>major.minor.patch</c>, digits and two dots only (no sign, space or other character the number parser
    /// would forgive), packed by <see cref="SemVerPacker"/>, whose order is the versions' order.
    /// </summary>
    private static bool TryParse(string? version, out uint packed)
    {
        packed = 0;
        return version is not null && version.All(c => c is >= '0' and <= '9' or '.') && SemVerPacker.TryPack(version, out packed);
    }
}
