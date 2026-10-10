namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// A context read with its standing: <see cref="Context"/> when it is current, null when it is not, and
/// <see cref="Unavailable"/> when the database could not tell (an outage), which is never a refusal.
/// <see cref="AccountRefusal"/> names why the context's account may not play (banned, deactivated, consolidating),
/// set only when the context is otherwise current on its own terms and its account still at the context's credentials
/// version (#882); <see cref="Context"/> is then null.
/// </summary>
public readonly record struct GameContextStanding(GameContextRecord? Context, bool Unavailable, string? AccountRefusal = null);
