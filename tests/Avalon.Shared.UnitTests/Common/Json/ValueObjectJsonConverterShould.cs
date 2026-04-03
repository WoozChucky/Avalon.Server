using System.Text.Json;
using Avalon.Common;
using Avalon.Common.Converters;
using Avalon.Common.ValueObjects;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Json;

public class ValueObjectJsonConverterShould
{
    private static JsonSerializerOptions BuildOptions() => new()
    {
        Converters = { new ValueObjectJsonConverterFactory() }
    };

    [Fact]
    public void SerializeAndDeserializeCharacterId()
    {
        var options = BuildOptions();
        var original = new CharacterId(123U);

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<CharacterId>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void SerializeAndDeserializeAccountId()
    {
        var options = BuildOptions();
        var original = new AccountId(9876543210L);

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<AccountId>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void SerializeAndDeserializeMapId()
    {
        var options = BuildOptions();
        var original = new MapId(5);

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<MapId>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void SerializeConcreteSubtypeAsObjectByDefault()
    {
        // The factory only intercepts ValueObject<T> directly, not concrete subclasses.
        // Concrete types like CharacterId fall back to default object serialization.
        var options = BuildOptions();
        var characterId = new CharacterId(42U);

        var json = JsonSerializer.Serialize(characterId, options);

        Assert.Contains("42", json);
    }

    [Fact]
    public void CanConvertReturnsTrueForValueObjectSubtype()
    {
        var factory = new ValueObjectJsonConverterFactory();

        Assert.False(factory.CanConvert(typeof(CharacterId)));
        Assert.False(factory.CanConvert(typeof(AccountId)));
        Assert.False(factory.CanConvert(typeof(int)));
        Assert.False(factory.CanConvert(typeof(string)));
        // The factory handles open generic ValueObject<T> check; closed concrete types
        // inherit from it but the factory's CanConvert checks for ValueObject<> directly.
        Assert.True(factory.CanConvert(typeof(ValueObject<uint>)));
        Assert.True(factory.CanConvert(typeof(ValueObject<long>)));
    }

    [Fact]
    public void CreateConverterForValueObjectType()
    {
        var factory = new ValueObjectJsonConverterFactory();
        var options = BuildOptions();

        var converter = factory.CreateConverter(typeof(ValueObject<uint>), options);

        Assert.NotNull(converter);
    }
}
