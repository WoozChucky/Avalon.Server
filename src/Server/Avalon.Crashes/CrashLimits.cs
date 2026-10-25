namespace Avalon.Crashes;

public static class CrashLimits
{
    public const int ArchiveBytes = 4 * 1024 * 1024;
    public const int MetadataBytes = 4 * 1024;
    public const int LogBytes = 64 * 1024;
    public const int StackBytes = 2 * 1024 * 1024;
    public const int Threads = 128;
    public const int FramesPerThread = 256;
    public const int InlineFrames = 16;
    public const int Modules = 512;
    public const int FunctionCharacters = 1024;
    public const int PathCharacters = 512;
    public const int ModuleCharacters = 128;
}
