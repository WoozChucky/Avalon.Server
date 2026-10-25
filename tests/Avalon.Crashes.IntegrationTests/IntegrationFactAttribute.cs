using Xunit;

namespace Avalon.Crashes.IntegrationTests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AVALON_CRASHES_INTEGRATION") != "1")
            Skip = "Use tools/crashes/run-integration.sh for isolated, explicitly enabled suite prerequisites.";
    }
}

public sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("AVALON_CRASHES_INTEGRATION") != "1")
            Skip = "Use tools/crashes/run-integration.sh for isolated, explicitly enabled suite prerequisites.";
    }
}
