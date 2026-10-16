using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class CommandLineShould
{
    /// <summary>
    /// A mix with quitters takes their times as given, each defaulting on its own (1 to 3 minutes in the world, 30 to 60
    /// seconds out), and the times are refused without a quitter to use them rather than silently ignored. A span is one
    /// duration or two joined by a dash, the shorter first; a stay of 0 would quit on entry, while a return at once is a
    /// test of its own.
    /// </summary>
    [Theory]
    [InlineData("--mix idle=50,quitter=50 --quit-after 2m-5m --rejoin-after 30s", "120-300 / 30-30")]
    [InlineData("--mix quitter=1 --rejoin-after 0", "60-180 / 0-0")]
    [InlineData("--mix quitter=1 --quit-after 90", "90-90 / 30-60")]
    [InlineData("", "none")]
    [InlineData("--quit-after 2m --rejoin-after 30s", "refused")]
    [InlineData("--mix quitter=1 --quit-after 5m-2m", "refused")]
    [InlineData("--mix quitter=1 --quit-after 0", "refused")]
    [InlineData("--mix quitter=1 --quit-after 1m-2m-3m", "refused")]
    [InlineData("--mix quitter=1 --rejoin-after 2h", "refused")]
    public void Take_a_quitters_times_only_when_the_mix_has_quitters(string args, string expected)
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
