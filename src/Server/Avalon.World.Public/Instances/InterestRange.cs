namespace Avalon.World.Public.Instances;

/// <summary>
/// How far a client sees (#593): objects enter its view within <see cref="Radius" /> metres of its own
/// character and leave it beyond <see cref="Radius" /> + <see cref="Margin" />, on X/Z.
/// </summary>
public readonly record struct InterestRange(float Radius, float Margin);
