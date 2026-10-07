using Avalon.Common;
using Xunit;

namespace Avalon.Shared.UnitTests.Common;

public class ObjectGuidShould
{
    [Fact]
    public void Be_empty_only_while_its_raw_value_is_zero()
    {
        var guid = new ObjectGuid();
        Assert.Equal(0UL, guid.RawValue);
        Assert.True(guid.IsEmpty);
        Assert.Equal(ObjectType.None, guid.Type);
        Assert.Equal(0U, guid.Id);

        const ulong Raw = 0x0100000000000001UL;
        Assert.Equal(Raw, new ObjectGuid(Raw).RawValue);
        Assert.False(new ObjectGuid(Raw).IsEmpty);

        guid.Set(ObjectType.Creature, 500U);
        Assert.Equal(ObjectType.Creature, guid.Type);
        Assert.Equal(500U, guid.Id);
        Assert.False(guid.IsEmpty);
    }

    [Theory]
    [InlineData(ObjectType.Character, 1U)]
    [InlineData(ObjectType.Creature, 9999U)]
    [InlineData(ObjectType.Spell, 0U)]
    [InlineData(ObjectType.SpellProjectile, uint.MaxValue)]
    public void EncodeTypeAndIdCorrectly(ObjectType type, uint id)
    {
        var guid = new ObjectGuid(type, id);
        Assert.Equal(type, guid.Type);
        // IdMask = 0x000000FFFFFFFFFF — only lower 40 bits are stored
        Assert.Equal(id & 0x000000FFFFFFFFFFUL, (ulong)guid.Id);
        Assert.False(guid.IsEmpty);
    }

    [Fact]
    public void RoundTripTypeAndIdThroughRawValue()
    {
        var original = new ObjectGuid(ObjectType.Character, 42U);
        var roundTripped = new ObjectGuid(original.RawValue);

        Assert.Equal(original.Type, roundTripped.Type);
        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.RawValue, roundTripped.RawValue);
    }

    [Fact]
    public void Compare_by_raw_value()
    {
        var a = new ObjectGuid(ObjectType.Character, 10U);
        var same = new ObjectGuid(ObjectType.Character, 10U);
        var other = new ObjectGuid(ObjectType.Character, 11U);
        ObjectGuid alias = a;
        ObjectGuid? none = null;
        ObjectGuid? alsoNone = null;

        Assert.True(a == same);
        Assert.False(a != same);
        Assert.True(a.Equals(same));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.False(a == other);
        Assert.True(a != other);
        Assert.False(a.Equals(other));
        Assert.True(a == alias);
        Assert.False(a == none);
        Assert.True(a != none);
        Assert.False(a.Equals(none));
        Assert.True(none == alsoNone);
        Assert.False(none != alsoNone);
        Assert.False(a.Equals("not a guid"));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void FormatToStringWithTypeAndId()
    {
        var guid = new ObjectGuid(ObjectType.Character, 42U);
        Assert.Equal("Type: Character, Id: 42", guid.ToString());
    }
}
