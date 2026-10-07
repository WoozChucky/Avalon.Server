using Avalon.Metrics;
using Xunit;

namespace Avalon.Shared.UnitTests.Metrics;

public class FakeMetricsManagerShould
{
    [Fact]
    public void Do_nothing_and_never_throw_even_disposed_twice()
    {
        var sut = new FakeMetricsManager();

        Exception? ex = Record.Exception(() =>
        {
            sut.Dispose();
            sut.Dispose();
            sut.Start();
            sut.Stop();
            sut.QueueEvent("e", "v");
            sut.QueueMetric("m", "v");
            sut.QueueMetric("m", 1.0);
            sut.QueueMetric("m", [0x01]);
            sut.SetDefaultProperties(new Dictionary<string, string>());
        });

        Assert.Null(ex);
    }
}
