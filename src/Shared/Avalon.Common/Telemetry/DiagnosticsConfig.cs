using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Avalon.Common.Telemetry;

public static class DiagnosticsConfig
{
    private const string FrameKicksName = "network.in.frame_kicks";

    private const string FrameKicksDescription =
        "Connections closed for a frame that declared more than Hosting:MaxInboundFrameSize or had a malformed length";

    private const string HandshakeFailuresName = "network.in.handshake_failures";

    private const string HandshakeFailuresDescription =
        "Connections dropped before their TLS handshake completed: it failed, or did not finish in time";

    private const string ProtocolKicksName = "network.in.protocol_kicks";

    private const string ProtocolKicksDescription =
        "Connections closed for a frame the peer got wrong: not a packet, sealed before or against the session key, a payload that is not its packet, or a frame not completed in time";

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

        public static readonly Counter<long> HandshakeFailures = Meter.CreateCounter<long>(HandshakeFailuresName,
            "{connections}", HandshakeFailuresDescription);

        /// <summary>
        /// Admitted connections closed for answering no time-sync ping for a minute (#1034, the liveness rule), through
        /// the drain a disconnect takes. No tags. docs/instrumentation.md.
        /// </summary>
        public static readonly Counter<long> SilentKicks = Meter.CreateCounter<long>("network.in.silent_kicks",
            "{connections}", "Admitted connections closed after a minute without a pong that answered a ping");

        /// <summary>
        /// Portal entries (<c>CMSG_ENTER_MAP</c>) the world refused, tagged <c>reason</c> from a fixed set
        /// (docs/instrumentation.md). Nothing a client sends, and no character, account or map, becomes a tag.
        /// </summary>
        public static readonly Counter<long> PortalEntryRefusals = Meter.CreateCounter<long>(
            "world.portal_entry.refusals", "{requests}", "Portal entries the world refused, by reason");

        /// <summary>
        /// Operations a full work queue (<c>WorldDatabaseWork</c>) refused (#1050), counted where they are refused,
        /// tagged <c>lane</c> from a fixed set: <c>admission</c>, <c>heartbeat</c>, <c>database</c> (and any lane added
        /// since). docs/instrumentation.md.
        /// </summary>
        public static readonly Counter<long> WorkRefusals = Meter.CreateCounter<long>(
            "world.work.refusals", "{operations}", "Operations a full work queue refused, by lane");

        /// <summary>
        /// Admissions whose redemption threw, or was cancelled, once started (#1050): answered
        /// <c>ServiceUnavailable</c>. A full admission queue is counted under <see cref="WorkRefusals" />.
        /// </summary>
        public static readonly Counter<long> AdmissionFailures = Meter.CreateCounter<long>(
            "world.admission.failures", "{admissions}", "Admissions whose redemption threw, answered ServiceUnavailable");

        /// <summary>
        /// Admissions refused before their ticket was redeemed because their source was over its budget (#1052):
        /// answered <c>ServiceUnavailable</c>. Tagged <c>reason</c>: <c>in_flight</c> (its cap, or its IPv6 /56's) or
        /// <c>rate</c> (its own, or its IPv6 /56's). Nothing a client sends, and no address, becomes a tag.
        /// </summary>
        public static readonly Counter<long> AdmissionSourceRefusals = Meter.CreateCounter<long>(
            "world.admission.source_refusals", "{admissions}",
            "Admissions refused because their source was over its admission budget, by reason, answered ServiceUnavailable");

        /// <summary>
        /// Admissions the world's backstop refused (#967): the world was over its player limit plus margin, and the
        /// account not staff. Answered <c>WorldFull</c>. Any value above zero means identity's place ledger was wrong or
        /// lost, or identity's stored limit is above the world's <c>Game:PlayerLimit</c>: docs/instrumentation.md. No tags.
        /// </summary>
        public static readonly Counter<long> BackstopRefusals = Meter.CreateCounter<long>(
            "world.admission.backstop_refusals", "{admissions}",
            "Admissions the world refused over its player limit plus margin: identity's place ledger was wrong or lost, or the limits differ");

        /// <summary>
        /// Instance builds abandoned at their deadline (#969, <c>InstanceRegistry.BuildDeadline</c>), every requester
        /// answered with the failure, counted once per build on the tick. Tagged <c>map.type</c> (<c>Town</c> or
        /// <c>Normal</c>). A server fault: docs/instrumentation.md.
        /// </summary>
        public static readonly Counter<long> InstanceBuildTimeouts = Meter.CreateCounter<long>(
            "world.instance.build_timeouts", "{builds}",
            "Instance builds abandoned after their deadline, their requesters answered with a failure");

        //public static ObservableGauge<long> ConnectedClients = Meter.CreateObservableGauge<long>("network.clients.connected");

        public static ActivitySource Source = new(ServiceName);
    }

    public static class Api
    {
        public const string ServiceName = "avalon-api";
        public static Meter Meter = new(ServiceName);

        /// <summary>Identity login decisions, tagged only by result and method. docs/instrumentation.md.</summary>
        public static readonly Counter<long> Logins = Meter.CreateCounter<long>(
            "avalon.api.logins", "{attempts}", "Identity password and MFA login decisions, by result and method");
    }
}
