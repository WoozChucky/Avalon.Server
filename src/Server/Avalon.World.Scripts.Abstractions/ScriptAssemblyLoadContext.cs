using System.Reflection;
using System.Runtime.Loader;

namespace Avalon.World.Scripts.Abstractions;

/// <summary>
/// The collectible load context one compile of the hot-reload scripts is loaded into (#930): one context per compile,
/// a generation. <see cref="AssemblyLoadContext.Unload" /> marks it for unloading; the runtime frees it, its types and
/// their compiled code once nothing references any of them any more (a type, an instance, a delegate).
/// </summary>
/// <remarks>
/// Every context made is tracked weakly, so <see cref="CountAlive" /> can tell how many are still in memory: the
/// current generation, and the replaced ones something still holds. Their dependencies (the server's own assemblies)
/// resolve from the default context, so a script's base types are the server's.
/// </remarks>
public sealed class ScriptAssemblyLoadContext : AssemblyLoadContext
{
    // Long weak references: a context has a finalizer, and stays in memory after it has run for as long as one of
    // its types is still referenced. A short one would read as gone while it is still loaded.
    private static readonly List<WeakReference<ScriptAssemblyLoadContext>> s_made = [];

    private volatile bool _unloading;

    public ScriptAssemblyLoadContext(string name) : base(name, isCollectible: true)
    {
        Unloading += static context => ((ScriptAssemblyLoadContext)context)._unloading = true;

        lock (s_made)
        {
            s_made.RemoveAll(static made => !made.TryGetTarget(out _));
            s_made.Add(new WeakReference<ScriptAssemblyLoadContext>(this, trackResurrection: true));
        }
    }

    /// <summary>Whether <see cref="AssemblyLoadContext.Unload" /> was called: it is waiting to be collected.</summary>
    public bool IsUnloading => _unloading;

    /// <summary>
    /// The contexts still in memory: <c>Current</c>, not unloaded (the generation in use), and <c>Unloaded</c>, unloaded
    /// and not yet collected. An unloaded one that stays for good is held by something that should have let it go.
    /// </summary>
    public static (int Current, int Unloaded) CountAlive()
    {
        int current = 0, unloaded = 0;
        lock (s_made)
        {
            for (int i = s_made.Count - 1; i >= 0; i--)
            {
                if (!s_made[i].TryGetTarget(out ScriptAssemblyLoadContext? context))
                {
                    s_made.RemoveAt(i);
                    continue;
                }

                if (context.IsUnloading) unloaded++;
                else current++;
            }
        }

        return (current, unloaded);
    }

    /// <summary>Nothing of its own: every dependency comes from the default context.</summary>
    protected override Assembly? Load(AssemblyName assemblyName) => null;
}
