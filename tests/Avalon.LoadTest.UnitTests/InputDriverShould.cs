using Avalon.LoadTest.Bots;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class InputDriverShould
{
    /// <summary>
    /// One loop that throws must stop them all, and fault the driver: a loop that died alone would leave its bots (and
    /// the bots given to it after) without input while the other loops kept the lateness the ramp judges looking fine.
    /// The bots' collection throws the first time a loop asks for it, so exactly one loop fails; the driver's own token
    /// is never cancelled, so only the failure can end the others.
    /// </summary>
    [Fact]
    public async Task Stop_every_loop_and_fault_when_one_loop_fails()
    {
        Bot[] none = [];
        int calls = 0;
        var driver = new InputDriver(
            () => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("loop failed") : none, loops: 4);

        Task driving = driver.RunAsync(CancellationToken.None);

        Assert.Same(driving, await Task.WhenAny(driving, Task.Delay(TimeSpan.FromSeconds(10))));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => driving);
        Assert.Equal("loop failed", error.Message);
        Assert.True(double.IsNaN(driver.LatenessP95Ms()), "a failed driver's lateness is unknown, so its step cannot pass");
    }
}
