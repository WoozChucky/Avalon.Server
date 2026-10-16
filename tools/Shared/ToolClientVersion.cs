namespace Avalon.Tools;

/// <summary>
/// The client version the tools that sign in as a game client say they are (#949), compiled into each of them
/// (<c>Avalon.LoadTest</c>, <c>Avalon.LocalDev</c>) from this one file: what the wire they speak is, never what the
/// services require. The services accept a client at or above <c>Application:GameWorkloads:MinClientVersion</c>, so this
/// stays at or above that floor (the API's default in <c>appsettings.json</c> and every deployment's) and moves with the
/// wire, as the game client's own version does.
/// </summary>
internal static class ToolClientVersion
{
    public const string Current = "0.3.0";
}
