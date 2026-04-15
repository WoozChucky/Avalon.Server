using Avalon.Metrics;
using Xunit;

namespace Avalon.Shared.UnitTests.Metrics;

public class FakeMetricsManagerShould
{
    [Fact]
    public void Should_NotThrow_WhenDisposedOnce()
    {
        var sut = new FakeMetricsManager();
        var ex = Record.Exception(() => sut.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Should_NotThrow_WhenDisposedTwice()
    {
        var sut = new FakeMetricsManager();
        sut.Dispose();
        var ex = Record.Exception(() => sut.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Should_RemainCallable_AfterDispose()
    {
        var sut = new FakeMetricsManager();
        sut.Dispose();

        var ex = Record.Exception(() =>
        {
            sut.Start();
            sut.Stop();
            sut.QueueEvent("e", "v");
            sut.QueueMetric("m", "v");
            sut.QueueMetric("m", 1.0);
            sut.QueueMetric("m", new byte[] { 0x01 });
            sut.SetDefaultProperties(new Dictionary<string, string>());
        });

        Assert.Null(ex);
    }
}
