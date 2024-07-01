using System.Threading;
using System.Threading.Tasks;

namespace Avalon.Hosting.PluginTypes;

public interface ISingletonPlugin
{
    Task InitializeAsync(CancellationToken token = default);
}
