using System.Net;
using System.Net.Sockets;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// Turns a caller's remote address into the addresses the login flow records and counts. The TCP
/// server has an endpoint string, the REST API an <see cref="IPAddress"/>; both name one address
/// the same way, so a source's budget is one key whichever server it guesses through.
/// </summary>
public static class RemoteAddress
{
    /// <summary>
    /// The full address, without the port, for IPv4 and IPv6 alike. An IPv4-mapped IPv6 address is
    /// given as the IPv4 address. A string that is not an endpoint is returned unchanged.
    /// </summary>
    public static string Of(string remoteEndPoint) =>
        TryParse(remoteEndPoint, out IPAddress? address) ? address.ToString() : remoteEndPoint;

    /// <inheritdoc cref="Of(string)"/>
    public static string Of(IPAddress address) => Unmap(address).ToString();

    /// <summary>
    /// The source a failed login is counted against: the full address for IPv4, the /64 prefix for
    /// IPv6, since one IPv6 host is normally given a whole /64 and could rotate through it.
    /// </summary>
    public static string SourceOf(string remoteEndPoint) =>
        TryParse(remoteEndPoint, out IPAddress? address) ? SourceOf(address) : remoteEndPoint;

    /// <inheritdoc cref="SourceOf(string)"/>
    public static string SourceOf(IPAddress address)
    {
        address = Unmap(address);
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    private static bool TryParse(string remoteEndPoint, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        if (!IPEndPoint.TryParse(remoteEndPoint, out IPEndPoint? endPoint))
            return false;

        address = Unmap(endPoint.Address);
        return true;
    }

    private static IPAddress Unmap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
