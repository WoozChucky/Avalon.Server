using Avalon.Common;
using Xunit;

namespace Avalon.Shared.UnitTests.Common;

public class ValueObjectShould
{
    private class TestValueObject(int value) : ValueObject<int>(value);

    private class TestStringValueObject(string value) : ValueObject<string>(value);

    [Fact]
    public void Compare_by_value()
    {
        var one = new TestValueObject(1);
        var alsoOne = new TestValueObject(1);
        var two = new TestValueObject(2);
        TestValueObject? none = null;

        Assert.Equal(one, alsoOne);
        Assert.True(one == alsoOne);
        Assert.False(one != alsoOne);
        Assert.True(one.Equals(alsoOne));
        Assert.Equal(one.GetHashCode(), alsoOne.GetHashCode());
        Assert.NotEqual(one, two);
        Assert.False(one == two);
        Assert.True(one != two);
        Assert.False(one.Equals(two));
        Assert.False(one == none);
        Assert.True(one != none);
        Assert.False(one.Equals(none));
        Assert.True(none == null);
        Assert.False(none != null);
    }

    [Fact]
    public void ThrowArgumentNullExceptionWhenValueIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TestStringValueObject(null!));
    }

    [Fact]
    public void Read_as_its_value()
    {
        var vo = new TestValueObject(123);
        int value = vo;

        Assert.Equal(123, value);
        Assert.Equal("123", vo.ToString());
    }
}
