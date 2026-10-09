namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// A context read with its standing: <see cref="Context"/> when it is current, null when it is not, and
/// <see cref="Unavailable"/> when the database could not tell (an outage), which is never a refusal.
/// </summary>
public readonly record struct GameContextStanding(GameContextRecord? Context, bool Unavailable);
