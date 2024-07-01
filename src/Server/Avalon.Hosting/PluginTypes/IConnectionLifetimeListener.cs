using System.Threading;
using System.Threading.Tasks;

namespace Avalon.Hosting.PluginTypes;

public interface IConnectionLifetimeListener
{
    Task OnConnectedAsync(CancellationToken token);
    Task OnDisconnectedAsync(CancellationToken token);
}
