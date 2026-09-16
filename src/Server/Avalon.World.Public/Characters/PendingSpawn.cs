using Avalon.World.Public.Instances;

namespace Avalon.World.Public.Characters;

/// <summary>
///     A character that character-select has finished building, held out of its instance until the
///     client reports the map loaded or the readiness barrier expires.
/// </summary>
/// <param name="Character">
///     The fully-initialized entity. It is not yet assigned to <c>IWorldConnection.Character</c>,
///     so nothing on the tick can see it.
/// </param>
/// <param name="Instance">The instance the character will be spawned into.</param>
/// <param name="SinceTicks"><c>DateTime.UtcNow.Ticks</c> at the moment the spawn became pending.</param>
public sealed record PendingSpawn(ICharacter Character, IMapInstance Instance, long SinceTicks);
