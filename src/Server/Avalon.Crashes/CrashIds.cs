using Avalon.Common;

namespace Avalon.Crashes;

public sealed class CrashReportId(Guid value) : ValueObject<Guid>(value)
{
    public static implicit operator CrashReportId(Guid value) => new(value);
}

public sealed class CrashGroupId(Guid value) : ValueObject<Guid>(value)
{
    public static implicit operator CrashGroupId(Guid value) => new(value);
}
