using System.Reflection;
using System.Runtime.Loader;

namespace Avalon.World.Scripts.Abstractions;

public class ScriptAssemblyLoadContext() : AssemblyLoadContext(isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        return null;
    }
}
