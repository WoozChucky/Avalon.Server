namespace Avalon.Api.Distribution;

/// <summary><c>channels/&lt;channel&gt;.json</c>: which build is current. Moving it is the release.</summary>
public sealed record ChannelPointer(string Build, string Manifest);
