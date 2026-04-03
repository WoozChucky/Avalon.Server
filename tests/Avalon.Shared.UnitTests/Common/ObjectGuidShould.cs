using Avalon.Common;
using Xunit;

namespace Avalon.Shared.UnitTests.Common;

public class ObjectGuidShould
{
    [Fact]
    public void BeEmptyByDefault()
    {
        var guid = new ObjectGuid();

        Assert.Equal(0UL, guid.RawValue);
        Assert.True(guid.IsEmpty);
        Assert.Equal(ObjectType.None, guid.Type);
        Assert.Equal(0U, guid.Id);
    }

    [Fact]
    public void StoreRawValue()
    {
        const ulong raw = 0x0100000000000001UL;
        var guid = new ObjectGuid(raw);

        Assert.Equal(raw, guid.RawValue);
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
    public void UpdateTypeAndIdOnSet()
    {
        var guid = new ObjectGuid(ObjectType.Character, 1U);

        guid.Set(ObjectType.Creature, 500U);

        Assert.Equal(ObjectType.Creature, guid.Type);
        Assert.Equal(500U, guid.Id);
    }

    [Fact]
    public void BeEqualWhenRawValuesMatch()
    {
        var a = new ObjectGuid(ObjectType.Character, 10U);
        var b = new ObjectGuid(ObjectType.Character, 10U);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void NotBeEqualWhenRawValuesDiffer()
    {
        var a = new ObjectGuid(ObjectType.Character, 10U);
        var b = new ObjectGuid(ObjectType.Character, 11U);

        Assert.False(a == b);
        Assert.True(a != b);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void HandleNullComparisonsGracefully()
    {
        var guid = new ObjectGuid(ObjectType.Character, 1U);
        ObjectGuid? nullGuid = null;

        Assert.False(guid == nullGuid);
        Assert.True(guid != nullGuid);
        Assert.False(guid.Equals(nullGuid));
    }

    [Fact]
    public void ReturnConsistentHashCode()
    {
        var a = new ObjectGuid(ObjectType.Creature, 7U);
        var b = new ObjectGuid(ObjectType.Creature, 7U);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void FormatToStringWithTypeAndId()
    {
        var guid = new ObjectGuid(ObjectType.Character, 42U);

        Assert.Equal("Type: Character, Id: 42", guid.ToString());
    }

    [Fact]
    public void TruncateIdToFortyBits()
    {
        // IdMask = 0x000000FFFFFFFFFF — any bits above bit 39 are masked out
        const uint id = 0xFFFFFFFFU;
        var guid = new ObjectGuid(ObjectType.Character, id);

        // Lower 40 bits of uint.MaxValue fit entirely, so Id should equal uint.MaxValue
        Assert.Equal(id & 0x000000FFFFFFFFU, (uint)(guid.Id & 0x000000FFFFFFFFU));
    }

    [Fact]
    public void TwoNullGuidsAreEqual()
    {
        ObjectGuid? a = null;
        ObjectGuid? b = null;

        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void ReferenceEqualityShortCircuitsOperator()
    {
        var guid = new ObjectGuid(ObjectType.Character, 1U);
        var same = guid;

        Assert.True(guid == same);
    }

    [Fact]
    public void IsNotEmptyAfterSetWithNonZeroId()
    {
        var guid = new ObjectGuid();
        guid.Set(ObjectType.Creature, 1U);

        Assert.False(guid.IsEmpty);
    }

    [Fact]
    public void EqualsReturnsFalseForNonGuidObject()
    {
        var guid = new ObjectGuid(ObjectType.Character, 1U);

        Assert.False(guid.Equals("not a guid"));
        Assert.False(guid.Equals(null));
    }
}
