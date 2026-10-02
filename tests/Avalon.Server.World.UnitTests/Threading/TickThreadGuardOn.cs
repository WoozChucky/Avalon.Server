using System.Runtime.CompilerServices;
using Avalon.World.Threading;

namespace Avalon.Server.World.UnitTests.Threading;

/// <summary>
/// The tick-thread assertion (#639) is on for this whole test assembly, in Debug and Release alike, so the guard tests
/// test it and every other test runs with it on. Only a guard that is bound refuses anything, and only the guard tests
/// bind one, so the rest are unaffected unless they really change tick-only state from another thread.
/// </summary>
internal static class TickThreadGuardOn
{
    [ModuleInitializer]
    internal static void Enable() => TickThreadGuard.Enable();
}
