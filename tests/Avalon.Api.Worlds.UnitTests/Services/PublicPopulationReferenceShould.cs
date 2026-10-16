using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Services;

/// <summary>Application:PublicPopulationReference is optional, but when given it must be a positive head count.</summary>
public sealed class PublicPopulationReferenceShould
{
    private static IServiceCollection Register(string? reference)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(reference is null
                ? []
                : new Dictionary<string, string?> { ["Application:PublicPopulationReference"] = reference })
            .Build();
        return new ServiceCollection().AddWorlds(configuration);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Stop_startup_when_not_positive(string reference)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Register(reference));

        Assert.Contains("Application:PublicPopulationReference", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("40")]
    public void Start_when_unset_or_positive(string? reference) => Assert.NotNull(Register(reference));
}
