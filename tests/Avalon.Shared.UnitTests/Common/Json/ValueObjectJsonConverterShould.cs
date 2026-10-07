using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>A value object over a wider or narrower primitive than the bare-scalar cases below.</summary>
    [Fact]
    public void Round_trip_value_objects_over_other_primitives()
    {
        JsonSerializerOptions options = BuildOptions();
        var account = new AccountId(9876543210L);
        var map = new MapId(5);

        Assert.Equal(account, JsonSerializer.Deserialize<AccountId>(JsonSerializer.Serialize(account, options), options));
        Assert.Equal(map, JsonSerializer.Deserialize<MapId>(JsonSerializer.Serialize(map, options), options));
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
        JsonSerializerOptions options = BuildOptions();

        string json = JsonSerializer.Serialize(new CharacterId(42U), options);

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
        JsonSerializerOptions options = BuildOptions();

        CharacterId? deserialized = JsonSerializer.Deserialize<CharacterId>("123", options);

        Assert.Equal(new CharacterId(123U), deserialized);
    }

    [Fact]
    public void Read_Null_As_Null()
    {
        JsonSerializerOptions options = BuildOptions();

        Assert.Null(JsonSerializer.Deserialize<CharacterId>("null", options));
    }

    [Fact]
    public void CreateConverterForValueObjectType()
    {
        var factory = new ValueObjectJsonConverterFactory();
        JsonSerializerOptions options = BuildOptions();

        JsonConverter converter = factory.CreateConverter(typeof(ValueObject<uint>), options);

        Assert.NotNull(converter);
    }
}
