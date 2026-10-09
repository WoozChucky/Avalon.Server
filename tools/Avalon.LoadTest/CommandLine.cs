namespace Avalon.LoadTest;

/// <summary>The tool's hand-parsed command line; each command adds its options here.</summary>
public static class CommandLine;

/// <summary>A command line the tool cannot run; the usage follows.</summary>
public sealed class CommandLineException(string message) : Exception(message);
