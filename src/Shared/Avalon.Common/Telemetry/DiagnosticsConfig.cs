using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Avalon.Common.Telemetry;

public static class DiagnosticsConfig
{
    private const string FrameKicksName = "network.in.frame_kicks";

    private const string FrameKicksDescription =
        "Connections closed for a frame that declared more than Hosting:MaxInboundFrameSize or had a malformed length";

    private const string ProtocolKicksName = "network.in.protocol_kicks";

    private const string ProtocolKicksDescription =
        "Connections closed for a frame the peer got wrong: not a packet, sealed before or against the session key, or a payload that is not its packet";

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

        public static readonly Counter<long> FrameKicks = Meter.CreateCounter<long>(FrameKicksName, "{connections}",
            FrameKicksDescription);

        public static readonly Counter<long> ProtocolKicks = Meter.CreateCounter<long>(ProtocolKicksName,
            "{connections}", ProtocolKicksDescription);

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

        public static readonly ActivitySource Source = new(ServiceName);

        public static readonly Counter<long> FrameKicks = Meter.CreateCounter<long>(FrameKicksName, "{connections}",
            FrameKicksDescription);

        public static readonly Counter<long> ProtocolKicks = Meter.CreateCounter<long>(ProtocolKicksName,
            "{connections}", ProtocolKicksDescription);

        public static readonly Counter<long> Logins =
            Meter.CreateCounter<long>("avalon.auth.logins", "{logins}", "Password logins, by result");
    }

    public static class Api
    {
        public const string ServiceName = "avalon-api";
        public static Meter Meter = new(ServiceName);
    }
}
