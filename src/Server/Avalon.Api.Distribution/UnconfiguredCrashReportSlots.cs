namespace Avalon.Api.Distribution;

public sealed class UnconfiguredCrashReportSlots : ICrashReportSlots
{
    public Uri PresignCrash(string key, long size, TimeSpan ttl) =>
        throw new DistributionUnavailableException("Crash uploads are not available right now.");
}
