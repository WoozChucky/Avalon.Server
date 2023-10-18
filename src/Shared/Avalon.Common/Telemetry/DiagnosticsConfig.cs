using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Avalon.Common.Telemetry;

public static class DiagnosticsConfig
{
    public static class Server
    {
        public const string ServiceName = "avalon-server";
        public static Meter Meter = new Meter(ServiceName);
    
        public static Histogram<double> PacketSentRate = Meter.CreateHistogram<double>("network.out.packets.rate");
        public static Histogram<double> PacketReceivedRate = Meter.CreateHistogram<double>("network.in.packets.rate");
    
        public static Histogram<double> BytesSentRate = Meter.CreateHistogram<double>("network.out.bytes.rate");
        public static Histogram<double> BytesReceivedRate = Meter.CreateHistogram<double>("network.in.bytes.rate");
    
        public static Counter<long> PacketsReceived = Meter.CreateCounter<long>("network.in.packets");
        public static Counter<long> PacketsSent = Meter.CreateCounter<long>("network.out.packets");
    
        public static Counter<long> BytesReceived = Meter.CreateCounter<long>("network.in.bytes");
        public static Counter<long> BytesSent = Meter.CreateCounter<long>("network.out.bytes");
    
        //public static ObservableGauge<long> ConnectedClients = Meter.CreateObservableGauge<long>("network.clients.connected");
    
        public static ActivitySource Source = new ActivitySource(ServiceName);
    }
    
    public static class Api
    {
        public const string ServiceName = "avalon-api";
        public static Meter Meter = new Meter(ServiceName);
    }
}
