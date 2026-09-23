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

    /// <summary>
    /// The whole point of the converter: a value object is its primitive on the wire. Every value
    /// object in the codebase is a concrete subclass -- CharacterId, AccountId, ItemTemplateId --
    /// so a factory that only matched the abstract base would never fire, and every id would
    /// serialise as {"value":42}. Asserting the exact JSON rather than Contains("42"), because
    /// {"value":42} contains "42" too.
    /// </summary>
    [Fact]
    public void Serialize_A_Concrete_Value_Object_As_A_Bare_Scalar()
    {
        var options = BuildOptions();

        var json = JsonSerializer.Serialize(new CharacterId(42U), options);

        Assert.Equal("42", json);
    }

    /// <summary>
    /// The concrete subclasses are the whole population that matters — nothing declares a property
    /// as the abstract <c>ValueObject&lt;T&gt;</c>. This previously asserted False for them, which
    /// pinned the defect rather than the contract.
    /// </summary>
    [Fact]
    public void Convert_Value_Object_Subclasses_And_Nothing_Else()
    {
        var factory = new ValueObjectJsonConverterFactory();

        Assert.True(factory.CanConvert(typeof(CharacterId)));
        Assert.True(factory.CanConvert(typeof(AccountId)));
        Assert.True(factory.CanConvert(typeof(MapId)));
        Assert.True(factory.CanConvert(typeof(ValueObject<uint>)));

        Assert.False(factory.CanConvert(typeof(int)));
        Assert.False(factory.CanConvert(typeof(string)));
        Assert.False(factory.CanConvert(typeof(object)));
    }

    /// <summary>
    /// The read direction, proved by handing it a bare scalar. The round-trip tests alone cannot
    /// prove it: object-shaped JSON round-trips through default serialization with no converter at
    /// all, which is exactly how they passed while the factory matched nothing.
    /// </summary>
    [Fact]
    public void Deserialize_A_Concrete_Value_Object_From_A_Bare_Scalar()
    {
        var options = BuildOptions();

        var deserialized = JsonSerializer.Deserialize<CharacterId>("123", options);

        Assert.Equal(new CharacterId(123U), deserialized);
    }

    [Fact]
    public void Read_Null_As_Null()
    {
        var options = BuildOptions();

        Assert.Null(JsonSerializer.Deserialize<CharacterId>("null", options));
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
