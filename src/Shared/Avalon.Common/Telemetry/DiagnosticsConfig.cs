using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Avalon.Common.Telemetry;

public static class DiagnosticsConfig
{
    public static class World
    {
        private const string ServiceName = "world-server";
        public static readonly Meter Meter = new(ServiceName);

        public static Counter<long> PacketsReceived =
            Meter.CreateCounter<long>("network.in.packets", "packets", "Number of packets received");

        public static Counter<long> PacketsSent =
            Meter.CreateCounter<long>("network.out.packets", "packets", "Number of packets sent");

        public static Counter<long> BytesReceived =
            Meter.CreateCounter<long>("network.in.bytes", "bytes", "Number of bytes received");

        public static Counter<long> BytesSent =
            Meter.CreateCounter<long>("network.out.bytes", "bytes", "Number of bytes sent");

        //public static ObservableGauge<long> ConnectedClients = Meter.CreateObservableGauge<long>("network.clients.connected");

        public static ActivitySource Source = new(ServiceName);
    }

    public static class Auth
    {
        private const string ServiceName = "auth-server";
        public static readonly Meter Meter = new(ServiceName);

        public static Counter<long> PacketsReceived =
            Meter.CreateCounter<long>("network.in.packets", "packets", "Number of packets received");

        public static Counter<long> PacketsSent =
            Meter.CreateCounter<long>("network.out.packets", "packets", "Number of packets sent");

        public static Counter<long> BytesReceived =
            Meter.CreateCounter<long>("network.in.bytes", "bytes", "Number of bytes received");

        public static Counter<long> BytesSent =
            Meter.CreateCounter<long>("network.out.bytes", "bytes", "Number of bytes sent");
    }

    public static class Api
    {
        public const string ServiceName = "avalon-api";
        public static Meter Meter = new(ServiceName);
    }
}
