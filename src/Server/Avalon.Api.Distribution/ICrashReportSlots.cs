namespace Avalon.Api.Distribution;

public interface ICrashReportSlots
{
    Uri PresignCrash(string key, long size, TimeSpan ttl);
}
