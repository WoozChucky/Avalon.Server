using System.Reflection;
using Avalon.World.Public.Creatures;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

public class CreatureMetadataShould
{
    /// <summary>
    /// Issue #438. Every creature of a type shares one <c>CreatureTemplate</c> as its
    /// <see cref="ICreatureMetadata"/>, so a write through <c>creature.Metadata</c> is a write to
    /// global state that every other creature of that type — and, after a reload, the live catalog —
    /// sees. <c>CreatureSpawner</c> did exactly that with <c>StartPosition</c>: twelve boars shared the
    /// twelfth one's spawn point. A read-only interface makes the compiler refuse the shape. The
    /// interface is also part of Avalon.World.Public, the future modding API, so a setter here would
    /// let a mod rewrite a creature type for everyone.
    /// </summary>
    [Fact]
    public void Expose_No_Setters()
    {
        string[] settable = typeof(ICreatureMetadata)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name)
            .ToArray();

        Assert.Empty(settable);
    }
}
