using Avalon.Common.Mathematics;
using Xunit;

namespace Avalon.Shared.UnitTests.Mathematics;

public class Vector2Should
{
    [Fact]
    public void Compare_by_components()
    {
        var v1 = new Vector2(3f, 4f);
        var v2 = new Vector2(3f, 4f);
        var other = new Vector2(1f, 2f);

        Assert.True(v1 == v2);
        Assert.False(v1 != v2);
        Assert.True(v1.Equals(v2));
        Assert.False(v1 == other);
        Assert.True(v1 != other);
        Assert.False(v1.Equals(other));
    }

    [Fact]
    public void Do_the_arithmetic_operators()
    {
        Assert.Equal(new Vector2(4f, 6f), new Vector2(1f, 2f) + new Vector2(3f, 4f));
        Assert.Equal(new Vector2(4f, 5f), new Vector2(5f, 7f) - new Vector2(1f, 2f));
        Assert.Equal(new Vector2(3f, 6f), new Vector2(1f, 2f) * 3f);
        Assert.Equal(new Vector2(3f, 6f), 3f * new Vector2(1f, 2f));
        Assert.Equal(new Vector2(2f, 4f), new Vector2(4f, 8f) / 2f);
        Vector2 negated = -new Vector2(1f, -2f);
        Assert.Equal(-1f, negated.x);
        Assert.Equal(2f, negated.y);
    }

    [Fact]
    public void Measure_length_distance_dot_and_angle()
    {
        var v = new Vector2(3f, 4f);

        Assert.Equal(5f, v.magnitude);
        Assert.Equal(25f, v.sqrMagnitude);
        Assert.Equal(v.sqrMagnitude, Vector2.SqrMagnitude(v));
        Assert.Equal(v.sqrMagnitude, v.SqrMagnitude());
        Assert.Equal(0f, Vector2.zero.magnitude);
        Assert.Equal(new Vector2(1f, 0f), new Vector2(5f, 0f).normalized);
        Assert.Equal(11f, Vector2.Dot(new Vector2(1f, 2f), v));
        Assert.Equal(5f, Vector2.Distance(Vector2.zero, v));
        Assert.Equal(90f, Vector2.Angle(new Vector2(1f, 0f), new Vector2(0f, 1f)), precision: 3);
    }

    [Fact]
    public void Interpolate_clamped_and_unclamped()
    {
        var from = new Vector2(0f, 0f);
        var to = new Vector2(10f, 10f);

        Assert.Equal(new Vector2(5f, 5f), Vector2.Lerp(from, to, 0.5f));
        Assert.Equal(from, Vector2.Lerp(from, to, -1f));
        Assert.Equal(to, Vector2.Lerp(from, to, 2f));
        Assert.Equal(new Vector2(20f, 20f), Vector2.LerpUnclamped(from, to, 2f));
    }

    [Fact]
    public void Scale_reflect_and_turn()
    {
        Assert.Equal(new Vector2(8f, 15f), Vector2.Scale(new Vector2(2f, 3f), new Vector2(4f, 5f)));
        var scaled = new Vector2(2f, 3f);
        scaled.Scale(new Vector2(4f, 5f));
        Assert.Equal(new Vector2(8f, 15f), scaled);

        // Reflecting (1,-1) over up normal (0,1) should give (1,1)
        var reflected = Vector2.Reflect(new Vector2(1f, -1f), new Vector2(0f, 1f));
        Assert.Equal(1f, reflected.x, precision: 5);
        Assert.Equal(1f, reflected.y, precision: 5);

        var perpendicular = Vector2.Perpendicular(new Vector2(1f, 0f));
        Assert.Equal(0f, perpendicular.x, precision: 5);
        Assert.Equal(1f, perpendicular.y, precision: 5);
    }

    [Fact]
    public void Clamp_the_magnitude_down_but_never_up()
    {
        Assert.Equal(2f, Vector2.ClampMagnitude(new Vector2(3f, 4f), 2f).magnitude, precision: 5);
        Assert.Equal(new Vector2(1f, 0f), Vector2.ClampMagnitude(new Vector2(1f, 0f), 5f));
    }

    [Fact]
    public void Take_the_component_wise_min_and_max()
    {
        var a = new Vector2(1f, 5f);
        var b = new Vector2(3f, 2f);

        Assert.Equal(new Vector2(1f, 2f), Vector2.Min(a, b));
        Assert.Equal(new Vector2(3f, 5f), Vector2.Max(a, b));
    }

    [Fact]
    public void Read_and_write_its_components()
    {
        var v = new Vector2(7f, 9f);
        Assert.Equal(7f, v[0]);
        Assert.Equal(9f, v[1]);
        Assert.Throws<IndexOutOfRangeException>(() => v[2]);

        v.Set(1f, 2f);
        Assert.Equal(1f, v.x);
        Assert.Equal(2f, v.y);

        Vector3 widened = new Vector2(3f, 4f);
        Assert.Equal(new Vector3(3f, 4f, 0f), widened);
    }
}
