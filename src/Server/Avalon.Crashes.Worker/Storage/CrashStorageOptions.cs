namespace Avalon.Crashes.Worker.Storage;

public sealed class CrashStorageOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string Region { get; set; } = "garage";
    public string Bucket { get; set; } = "crashes";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
}
