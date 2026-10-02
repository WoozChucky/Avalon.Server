namespace Avalon.World.Social;

/// <summary>One character on an ignore list: its id, its name when the entry was loaded or added, and when it was added.</summary>
public sealed record IgnoredCharacter(uint Id, string Name, DateTime CreatedAt);
