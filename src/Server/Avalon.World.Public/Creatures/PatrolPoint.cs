using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Creatures;

/// <summary>
/// One point on a creature's patrol path: where to walk to, and how long to stand there before
/// walking on. <see cref="TimeSpan.Zero"/> means walk straight on.
/// </summary>
public readonly record struct PatrolPoint(Vector3 Position, TimeSpan Wait);
