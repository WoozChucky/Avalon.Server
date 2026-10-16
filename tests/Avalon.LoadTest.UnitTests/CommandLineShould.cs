using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class CommandLineShould
{
    /// <summary>
    /// A quitter's times have no default (the limit test chooses them), so a mix with quitters is refused without both,
    /// and the times are refused without a quitter to use them rather than silently ignored. A span is one duration or
    /// two joined by a dash, the shorter first; a stay of 0 would quit on entry, while a return at once is a test of its own.
    /// </summary>
    [Theory]
    [InlineData("--mix idle=50,quitter=50 --quit-after 2m-5m --rejoin-after 30s", "120-300 / 30-30")]
    [InlineData("--mix quitter=1 --quit-after 90 --rejoin-after 0", "90-90 / 0-0")]
    [InlineData("", "none")]
    [InlineData("--mix idle=50,quitter=50", "refused")]
    [InlineData("--mix idle=50,quitter=50 --quit-after 2m", "refused")]
    [InlineData("--quit-after 2m --rejoin-after 30s", "refused")]
    [InlineData("--mix idle=50,quitter=0 --quit-after 2m --rejoin-after 30s", "refused")]
    [InlineData("--mix quitter=1 --quit-after 5m-2m --rejoin-after 30s", "refused")]
    [InlineData("--mix quitter=1 --quit-after 0 --rejoin-after 30s", "refused")]
    [InlineData("--mix quitter=1 --quit-after 1m-2m-3m --rejoin-after 30s", "refused")]
    [InlineData("--mix quitter=1 --quit-after 2m --rejoin-after 2h", "refused")]
    public void Need_a_quitters_times_exactly_when_the_mix_has_quitters(string args, string expected)
    {
        string parsed;
        try
        {
            RampArguments ramp = CommandLine.ParseRamp(args.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            parsed = ramp.Quits is { } quits
                ? $"{quits.Stay.Min.TotalSeconds}-{quits.Stay.Max.TotalSeconds} / {quits.Away.Min.TotalSeconds}-{quits.Away.Max.TotalSeconds}"
                : "none";
        }
        catch (CommandLineException)
        {
            parsed = "refused";
        }

        Assert.Equal(expected, parsed);
    }
}
