namespace Avalon.World.Social;

/// <summary>The system lines the ignore commands answer with (#723).</summary>
public static class IgnoreLines
{
    public const string IgnoreUsage = "Usage: /ignore <name>";
    public const string UnignoreUsage = "Usage: /unignore <name>";
    public const string Self = "You can't ignore yourself.";
    public const string Empty = "You are not ignoring anyone.";

    public static string Added(string name) => $"You are now ignoring {name}.";
    public static string Removed(string name) => $"You are no longer ignoring {name}.";
    public static string Already(string name) => $"{name} is already on your ignore list.";
    public static string NotOnList(string name) => $"{name} is not on your ignore list.";
    public static string NoSuchCharacter(string name) => $"No character named {name} exists.";
    public static string Full(int max) => $"Your ignore list is full ({max}/{max}).";

    public static string Listing(IgnoreList list, int max) =>
        $"Ignoring {list.Count}/{max}: {string.Join(", ", list.Entries.Select(e => e.Name))}.";
}
