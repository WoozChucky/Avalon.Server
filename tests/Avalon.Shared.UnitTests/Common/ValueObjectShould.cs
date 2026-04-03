using Avalon.Common;
using Xunit;

namespace Avalon.Shared.UnitTests.Common;

public class ValueObjectShould
{
    private class TestValueObject(int value) : ValueObject<int>(value);
    private class TestStringValueObject(string value) : ValueObject<string>(value);

    [Fact]
    public void BeEqualWhenValuesAreEqual()
    {
        var vo1 = new TestValueObject(1);
        var vo2 = new TestValueObject(1);

        Assert.Equal(vo1, vo2);
        Assert.True(vo1 == vo2);
        Assert.False(vo1 != vo2);
        Assert.True(vo1.Equals(vo2));
    }

    [Fact]
    public void NotBeEqualWhenValuesAreDifferent()
    {
        var vo1 = new TestValueObject(1);
        var vo2 = new TestValueObject(2);

        Assert.NotEqual(vo1, vo2);
        Assert.False(vo1 == vo2);
        Assert.True(vo1 != vo2);
        Assert.False(vo1.Equals(vo2));
    }

    [Fact]
    public void HandleNullComparison()
    {
        var vo1 = new TestValueObject(1);
        TestValueObject? vo2 = null;

        Assert.False(vo1 == vo2);
        Assert.True(vo1 != vo2);
        Assert.False(vo1.Equals(vo2));
        
        Assert.True(vo2 == null);
        Assert.False(vo2 != null);
    }

    [Fact]
    public void ThrowArgumentNullExceptionWhenValueIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TestStringValueObject(null!));
    }

    [Fact]
    public void ReturnCorrectHashCode()
    {
        var vo1 = new TestValueObject(1);
        var vo2 = new TestValueObject(1);

        Assert.Equal(vo1.GetHashCode(), vo2.GetHashCode());
    }

    [Fact]
    public void ReturnCorrectToString()
    {
        var vo1 = new TestValueObject(123);
        Assert.Equal("123", vo1.ToString());
    }

    [Fact]
    public void ImplicitlyConvertToUnderlyingType()
    {
        var vo = new TestValueObject(123);
        int value = vo;

        Assert.Equal(123, value);
    }
}
