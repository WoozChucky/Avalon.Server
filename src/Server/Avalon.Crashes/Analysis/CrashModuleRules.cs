namespace Avalon.Crashes.Analysis;

// These lists are part of signature algorithm v1. Changes require a new signature version.
public static class CrashModuleRules
{
    public const int SignatureVersion = 1;

    private static readonly HashSet<string> s_systemModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntdll.dll", "kernel32.dll", "kernelbase.dll", "ucrtbase.dll", "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"
    };

    private static readonly HashSet<string> s_driverModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvoglv64.dll", "nvwgf2umx.dll", "nvldumdx.dll", "nvd3dumx.dll",
        "amdvlk64.dll", "amdvlk32.dll", "atio6axx.dll", "atidxx64.dll", "aticfx64.dll",
        "igvk64.dll", "igvk32.dll", "igc64.dll", "igd10iumd64.dll", "igd12umd64.dll"
    };

    // Exact names from engine/src/platform/CrashHandler.cpp. Trigger and Overflow are fault sites,
    // not capture plumbing. Keep them actionable. Anonymous-namespace spelling is normalized below.
    private static readonly HashSet<string> s_runtimeNoise = new(StringComparer.Ordinal)
    {
        "Avalon::Engine::CrashHandler::Capture", "Avalon::Engine::CrashHandler::Unhandled",
        "Avalon::Engine::CrashHandler::Synthetic", "Avalon::Engine::CrashHandler::Terminated",
        "Avalon::Engine::CrashHandler::PureCall", "Avalon::Engine::CrashHandler::InvalidParameter",
        "Avalon::Engine::CrashHandler::Writer", "Avalon::Engine::CrashHandler::CaptureCaughtException"
    };

    public static bool IsRuntime(string? module) => string.Equals(module, "runtime.exe", StringComparison.OrdinalIgnoreCase);
    public static bool IsSystem(string? module) => module is not null && s_systemModules.Contains(module);
    public static bool IsGpuDriver(string? module) => module is not null && s_driverModules.Contains(module);

    internal static bool IsNoise(CrashFrame frame)
    {
        if (IsSystem(frame.Module))
            return true;
        if (!IsRuntime(frame.Module) || frame.Function is null)
            return false;
        string function = frame.Function.Replace("(anonymous namespace)::", "", StringComparison.Ordinal)
            .Replace("`anonymous namespace'::", "", StringComparison.Ordinal);
        int arguments = function.IndexOf('(');
        return s_runtimeNoise.Contains((arguments < 0 ? function : function[..arguments]).Trim());
    }
}
