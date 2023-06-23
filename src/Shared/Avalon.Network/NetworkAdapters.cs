using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Avalon.Network;

public static class NetworkAdapters
{
    public static IPAddress GetLocalIpAddress()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Only consider Ethernet and WLAN network interfaces
            if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
            {
                continue;
            }

            // Skip network interfaces that are down
            if (ni.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var ipProps = ni.GetIPProperties();
            foreach (var addr in ipProps.UnicastAddresses)
            {
                // Only consider IPv4 addresses that aren't loopback addresses
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr.Address))
                {
                    return addr.Address;
                }
            }
        }

        throw new Exception("No suitable network interface found!");
    }
}
