using Avalon.World.Threading;
using Microsoft.Extensions.Options;

namespace Avalon.World.Configuration;

/// <summary>
/// Refuses <c>Game:TickThreadCpu</c> (#1002) when this machine cannot give the tick that CPU: one the process may not run
/// on, or one whose core is every CPU it has. Its range is the annotation's to check; elsewhere than on Linux nothing is
/// refused here, and the tick runs unpinned with a warning.
/// </summary>
public sealed class TickThreadCpuValidation(ICpuAffinity os) : IValidateOptions<GameConfiguration>
{
    public ValidateOptionsResult Validate(string? name, GameConfiguration options)
    {
        if (options.TickThreadCpu is not int cpu || cpu is < 0 or > TickThreadPlacement.MaxCpu)
            return ValidateOptionsResult.Skip;

        string? refusal = TickThreadPlacement.Refusal(cpu, os);
        return refusal is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(refusal);
    }
}
