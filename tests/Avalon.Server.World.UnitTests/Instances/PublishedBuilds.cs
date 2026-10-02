using Avalon.World.Public.Instances;

namespace Avalon.Server.World.UnitTests.Instances;

internal static class PublishedBuilds
{
    /// <summary>
    /// Publishes what the world's registry has built, as the tick does first thing (#639), and hands back
    /// <paramref name="pending" />. For a build that finished at once (a factory returning a completed task), the task is
    /// complete when this returns.
    /// </summary>
    public static Task<IMapInstance> Published(this Task<IMapInstance> pending, Avalon.World.World world)
    {
        world.PublishBuiltInstances();
        return pending;
    }
}
