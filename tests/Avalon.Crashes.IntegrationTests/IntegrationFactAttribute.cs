using Xunit;

namespace Avalon.Crashes.IntegrationTests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AVALON_CRASHES_INTEGRATION") != "1")
            Skip = "Opt in with AVALON_CRASHES_INTEGRATION=1 and AVALON_CRASHES_TEST_POSTGRES.";
    }
}
