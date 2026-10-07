namespace Avalon.Hosting.PluginTypes;

public interface IGameTickListener
{
    Task PreUpdateAsync(CancellationToken token);
    Task PostUpdateAsync(CancellationToken token);
}
