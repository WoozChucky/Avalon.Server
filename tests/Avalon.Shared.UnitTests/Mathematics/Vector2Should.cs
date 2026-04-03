using Avalon.Common.Mathematics;
using Xunit;

namespace Avalon.Shared.UnitTests.Mathematics;

public class Vector2Should
{
    [Fact]
    public void BeEqualWhenComponentsAreEqual()
    {
        var v1 = new Vector2(3f, 4f);
        var v2 = new Vector2(3f, 4f);

        Assert.True(v1 == v2);
        Assert.False(v1 != v2);
        Assert.True(v1.Equals(v2));
    }

    [Fact]
    public void NotBeEqualWhenComponentsAreDifferent()
    {
        var v1 = new Vector2(1f, 2f);
        var v2 = new Vector2(3f, 4f);

        Assert.False(v1 == v2);
        Assert.True(v1 != v2);
        Assert.False(v1.Equals(v2));
    }

    [Fact]
    public void SupportAddition()
    {
        var v1 = new Vector2(1f, 2f);
        var v2 = new Vector2(3f, 4f);
        var expected = new Vector2(4f, 6f);

        Assert.Equal(expected, v1 + v2);
    }

    [Fact]
    public void SupportSubtraction()
    {
        var v1 = new Vector2(5f, 7f);
        var v2 = new Vector2(1f, 2f);
        var expected = new Vector2(4f, 5f);

        Assert.Equal(expected, v1 - v2);
    }

    [Fact]
    public void SupportMultiplicationByScalar()
    {
        var v = new Vector2(1f, 2f);
        var expected = new Vector2(3f, 6f);

        Assert.Equal(expected, v * 3f);
        Assert.Equal(expected, 3f * v);
    }

    [Fact]
    public void SupportDivisionByScalar()
    {
        var v = new Vector2(4f, 8f);
        var expected = new Vector2(2f, 4f);

        Assert.Equal(expected, v / 2f);
    }

    [Fact]
    public void CalculateCorrectMagnitude()
    {
        var v = new Vector2(3f, 4f);

        Assert.Equal(5f, v.magnitude);
        Assert.Equal(25f, v.sqrMagnitude);
    }

    [Fact]
    public void SupportNormalization()
    {
        var v = new Vector2(5f, 0f);
        var normalized = v.normalized;

        Assert.Equal(new Vector2(1f, 0f), normalized);
        Assert.Equal(1f, normalized.magnitude, precision: 5);
    }

    [Fact]
    public void ReturnCorrectDotProduct()
    {
        var v1 = new Vector2(1f, 2f);
        var v2 = new Vector2(3f, 4f);
        // 1*3 + 2*4 = 3 + 8 = 11
        Assert.Equal(11f, Vector2.Dot(v1, v2));
    }

    [Fact]
    public void ReturnCorrectDistance()
    {
        var v1 = new Vector2(0f, 0f);
        var v2 = new Vector2(3f, 4f);

        Assert.Equal(5f, Vector2.Distance(v1, v2));
    }

    [Fact]
    public void SupportLerp()
    {
        var v1 = new Vector2(0f, 0f);
        var v2 = new Vector2(10f, 10f);
        var result = Vector2.Lerp(v1, v2, 0.5f);

        Assert.Equal(new Vector2(5f, 5f), result);
    }

    [Fact]
    public void LerpClampsTToZeroOne()
    {
        var v1 = new Vector2(0f, 0f);
        var v2 = new Vector2(10f, 10f);

        Assert.Equal(v1, Vector2.Lerp(v1, v2, -1f));
        Assert.Equal(v2, Vector2.Lerp(v1, v2, 2f));
    }

    [Fact]
    public void ReturnZeroForZeroVector()
    {
        var v = Vector2.zero;

        Assert.Equal(0f, v.magnitude);
        Assert.Equal(0f, v.x);
        Assert.Equal(0f, v.y);
    }

    [Fact]
    public void AccessComponentsByIndex()
    {
        var v = new Vector2(7f, 9f);

        Assert.Equal(7f, v[0]);
        Assert.Equal(9f, v[1]);
    }

    [Fact]
    public void ThrowOnOutOfRangeIndex()
    {
        var v = new Vector2(1f, 2f);

        Assert.Throws<IndexOutOfRangeException>(() => v[2]);
    }

    // ── Additional static methods ─────────────────────────────────────────────

    [Fact]
    public void LerpUnclampedExtendsRange()
    {
        var v1 = new Vector2(0f, 0f);
        var v2 = new Vector2(10f, 10f);

        var result = Vector2.LerpUnclamped(v1, v2, 2f);
        Assert.Equal(new Vector2(20f, 20f), result);
    }

    [Fact]
    public void ScaleMultipliesComponentWise()
    {
        var a = new Vector2(2f, 3f);
        var b = new Vector2(4f, 5f);

        var result = Vector2.Scale(a, b);

        Assert.Equal(new Vector2(8f, 15f), result);
    }

    [Fact]
    public void ScaleInstanceMutatesComponents()
    {
        var v = new Vector2(2f, 3f);
        v.Scale(new Vector2(4f, 5f));

        Assert.Equal(8f, v.x, precision: 5);
        Assert.Equal(15f, v.y, precision: 5);
    }

    [Fact]
    public void ReflectVectorAcrossNormal()
    {
        // Reflecting (1,-1) over up normal (0,1) should give (1,1)
        var inDir = new Vector2(1f, -1f);
        var normal = new Vector2(0f, 1f);

        var result = Vector2.Reflect(inDir, normal);

        Assert.Equal(1f, result.x, precision: 5);
        Assert.Equal(1f, result.y, precision: 5);
    }

    [Fact]
    public void PerpendicularRotates90Degrees()
    {
        var v = new Vector2(1f, 0f);
        var perp = Vector2.Perpendicular(v);

        Assert.Equal(0f, perp.x, precision: 5);
        Assert.Equal(1f, perp.y, precision: 5);
    }

    [Fact]
    public void AngleBetweenOrthogonalVectorsIs90()
    {
        var v1 = new Vector2(1f, 0f);
        var v2 = new Vector2(0f, 1f);

        float angle = Vector2.Angle(v1, v2);

        Assert.Equal(90f, angle, precision: 3);
    }

    [Fact]
    public void ClampMagnitudeScalesDown()
    {
        var v = new Vector2(3f, 4f); // magnitude=5
        var clamped = Vector2.ClampMagnitude(v, 2f);

        Assert.Equal(2f, clamped.magnitude, precision: 5);
    }

    [Fact]
    public void ClampMagnitudeDoesNotScaleUp()
    {
        var v = new Vector2(1f, 0f); // magnitude=1
        var clamped = Vector2.ClampMagnitude(v, 5f);

        Assert.Equal(v, clamped);
    }

    [Fact]
    public void MinReturnsComponentWiseMinimum()
    {
        var a = new Vector2(1f, 5f);
        var b = new Vector2(3f, 2f);

        var result = Vector2.Min(a, b);

        Assert.Equal(new Vector2(1f, 2f), result);
    }

    [Fact]
    public void MaxReturnsComponentWiseMaximum()
    {
        var a = new Vector2(1f, 5f);
        var b = new Vector2(3f, 2f);

        var result = Vector2.Max(a, b);

        Assert.Equal(new Vector2(3f, 5f), result);
    }

    [Fact]
    public void SetMutatesComponents()
    {
        var v = new Vector2(1f, 2f);
        v.Set(7f, 9f);

        Assert.Equal(7f, v.x);
        Assert.Equal(9f, v.y);
    }

    [Fact]
    public void ImplicitConversionToVector3SetsZToZero()
    {
        var v2 = new Vector2(3f, 4f);
        Vector3 v3 = v2;

        Assert.Equal(3f, v3.x);
        Assert.Equal(4f, v3.y);
        Assert.Equal(0f, v3.z);
    }

    [Fact]
    public void StaticSqrMagnitudeMatchesInstanceSqrMagnitude()
    {
        var v = new Vector2(3f, 4f);

        Assert.Equal(v.sqrMagnitude, Vector2.SqrMagnitude(v));
        Assert.Equal(v.sqrMagnitude, v.SqrMagnitude());
    }

    [Fact]
    public void NegationFlipsSign()
    {
        var v = new Vector2(1f, -2f);
        var neg = -v;

        Assert.Equal(-1f, neg.x);
        Assert.Equal(2f, neg.y);
    }
}
