using System.Reflection;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// #612: replication is server-internal. World.Public is the modding API, and a mod that could drive a
/// character's replication state could change what that player's client is told it can see, so the
/// state and its update live World-side only.
/// </summary>
public class ReplicationSurfaceShould
{
    private static readonly Assembly ModdingApi = typeof(ICharacter).Assembly;

    [Fact]
    public void Expose_no_replication_state_on_a_character_in_the_modding_api()
    {
        Assert.DoesNotContain(ModdingApi.GetTypes(), t => t.Name == "ICharacterGameState");
        Assert.Null(typeof(ICharacter).GetProperty("CharacterGameState"));
    }

    [Fact]
    public void Offer_no_method_in_the_modding_api_that_diffs_a_view_by_interest_range()
    {
        IEnumerable<string> diffs = ModdingApi.GetTypes()
            .Where(t => t != typeof(InterestRange))   // its own record members take one
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(InterestRange)))
                .Select(m => $"{t.FullName}.{m.Name}"));

        Assert.Empty(diffs);
    }
}
