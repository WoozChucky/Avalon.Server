using System.Text.Json;
using Avalon.Common.Converters;
using Avalon.Common.ValueObjects;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Json;

public class JsonSerializerOptionsFactoryShould
{
    [Fact]
    public void CreateReturnsNonNullOptions()
    {
        var options = JsonSerializerOptionsFactory.Create();
        Assert.NotNull(options);
    }

    [Fact]
    public void CreateIncludesValueObjectConverterFactory()
    {
        var options = JsonSerializerOptionsFactory.Create();

        Assert.Contains(options.Converters, c => c is ValueObjectJsonConverterFactory);
    }

    [Fact]
    public void StaticSettingsPropertyIsNonNull()
    {
        Assert.NotNull(JsonSerializerOptionsFactory.Settings);
    }

    [Fact]
    public void StaticSettingsIsSameInstanceOnSecondAccess()
    {
        var s1 = JsonSerializerOptionsFactory.Settings;
        var s2 = JsonSerializerOptionsFactory.Settings;

        Assert.Same(s1, s2);
    }

    [Fact]
    public void CreateProducesIndependentInstances()
    {
        var o1 = JsonSerializerOptionsFactory.Create();
        var o2 = JsonSerializerOptionsFactory.Create();

        Assert.NotSame(o1, o2);
    }

    [Fact]
    public void SettingsCanSerializeCharacterId()
    {
        var options = JsonSerializerOptionsFactory.Settings;
        var id = new CharacterId(77U);

        // Concrete subtypes serialize as an object via default; the factory
        // handles the open ValueObject<T> generic form.
        var json = JsonSerializer.Serialize(id, options);

        Assert.Contains("77", json);
    }
}
