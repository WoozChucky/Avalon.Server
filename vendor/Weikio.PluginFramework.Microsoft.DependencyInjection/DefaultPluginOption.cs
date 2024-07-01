using System;
using System.Collections.Generic;
using System.Linq;

namespace Weikio.PluginFramework.Microsoft.DependencyInjection;

public class DefaultPluginOption
{
    public Func<IServiceProvider, IEnumerable<Type>, Type> DefaultType { get; set; } 
        = (serviceProvider, implementationTypes) => implementationTypes.FirstOrDefault();
}
