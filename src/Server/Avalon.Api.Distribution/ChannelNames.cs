namespace Avalon.Api.Distribution;

public static class ChannelNames
{
    public static string Wire(this Channel channel) => channel switch
    {
        Channel.Live => "live",
        Channel.Ptr => "ptr",
        Channel.Dev => "dev",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public static bool TryParse(string? value, out Channel channel)
    {
        switch (value)
        {
            case "live": channel = Channel.Live; return true;
            case "ptr": channel = Channel.Ptr; return true;
            case "dev": channel = Channel.Dev; return true;
            default: channel = default; return false;
        }
    }
}
