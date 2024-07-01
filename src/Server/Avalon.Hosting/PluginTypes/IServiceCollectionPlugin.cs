using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Hosting.PluginTypes;

public interface IServiceCollectionPlugin
{
    void ModifyServiceCollection(IServiceCollection services);
}
