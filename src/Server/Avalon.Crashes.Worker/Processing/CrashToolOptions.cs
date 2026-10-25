namespace Avalon.Crashes.Worker.Processing;

public sealed record CrashToolOptions
{
    public string ToolDirectory { get; init; } = "/opt/avalon-native";
    public string ScratchRoot { get; init; } = "/tmp/avalon-crashes";
    public long ScratchLimitBytes { get; init; } = 2L * 1024 * 1024 * 1024;
}
