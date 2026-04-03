using Avalon.Common.Mathematics;
using Xunit;

namespace Avalon.Shared.UnitTests.Mathematics;

public class Vector3Should
{
    [Fact]
    public void BeEqualWhenComponentsAreEqual()
    {
        var v1 = new Vector3(1f, 2f, 3f);
        var v2 = new Vector3(1f, 2f, 3f);

        Assert.Equal(v1, v2);
        Assert.True(v1 == v2);
        Assert.False(v1 != v2);
    }

    [Fact]
    public void NotBeEqualWhenComponentsAreDifferent()
    {
        var v1 = new Vector3(1f, 2f, 3f);
        var v2 = new Vector3(4f, 5f, 6f);

        Assert.NotEqual(v1, v2);
        Assert.False(v1 == v2);
        Assert.True(v1 != v2);
    }

    [Fact]
    public void SupportAddition()
    {
        var v1 = new Vector3(1f, 2f, 3f);
        var v2 = new Vector3(4f, 5f, 6f);
        var expected = new Vector3(5f, 7f, 9f);

        Assert.Equal(expected, v1 + v2);
    }

    [Fact]
    public void SupportSubtraction()
    {
        var v1 = new Vector3(5f, 7f, 9f);
        var v2 = new Vector3(1f, 2f, 3f);
        var expected = new Vector3(4f, 5f, 6f);

        Assert.Equal(expected, v1 - v2);
    }

    [Fact]
    public void SupportMultiplicationByScalar()
    {
        var v = new Vector3(1f, 2f, 3f);
        var expected = new Vector3(2f, 4f, 6f);

        Assert.Equal(expected, v * 2f);
        Assert.Equal(expected, 2f * v);
    }

    [Fact]
    public void SupportDivisionByScalar()
    {
        var v = new Vector3(2f, 4f, 6f);
        var expected = new Vector3(1f, 2f, 3f);

        Assert.Equal(expected, v / 2f);
    }

    [Fact]
    public void CalculateCorrectMagnitude()
    {
        var v = new Vector3(3f, 0f, 4f);
        Assert.Equal(5f, v.magnitude);
        Assert.Equal(25f, v.sqrMagnitude);
    }

    [Fact]
    public void SupportNormalization()
    {
        var v = new Vector3(5f, 0f, 0f);
        var normalized = v.normalized;

        Assert.Equal(new Vector3(1f, 0f, 0f), normalized);
        Assert.Equal(1f, normalized.magnitude);
    }

    [Fact]
    public void ReturnCorrectDotProduct()
    {
        var v1 = new Vector3(1f, 2f, 3f);
        var v2 = new Vector3(4f, 5f, 6f);
        // 1*4 + 2*5 + 3*6 = 4 + 10 + 18 = 32
        Assert.Equal(32f, Vector3.Dot(v1, v2));
    }

    [Fact]
    public void ReturnCorrectDistance()
    {
        var v1 = new Vector3(0f, 0f, 0f);
        var v2 = new Vector3(3f, 0f, 4f);
        Assert.Equal(5f, Vector3.Distance(v1, v2));
    }

    [Fact]
    public void SupportLerp()
    {
        var v1 = new Vector3(0f, 0f, 0f);
        var v2 = new Vector3(10f, 10f, 10f);
        var result = Vector3.Lerp(v1, v2, 0.5f);

        Assert.Equal(new Vector3(5f, 5f, 5f), result);
    }

    // ── Static methods ────────────────────────────────────────────────────────

    [Fact]
    public void LerpClampsTToZeroOne()
    {
        var v1 = new Vector3(0f, 0f, 0f);
        var v2 = new Vector3(10f, 10f, 10f);

        Assert.Equal(v1, Vector3.Lerp(v1, v2, -1f));
        Assert.Equal(v2, Vector3.Lerp(v1, v2, 2f));
    }

    [Fact]
    public void LerpUnclampedExtendsRange()
    {
        var v1 = new Vector3(0f, 0f, 0f);
        var v2 = new Vector3(10f, 10f, 10f);

        var result = Vector3.LerpUnclamped(v1, v2, 2f);
        Assert.Equal(new Vector3(20f, 20f, 20f), result);
    }

    [Fact]
    public void CrossProductIsOrthogonal()
    {
        var right = new Vector3(1f, 0f, 0f);
        var up = new Vector3(0f, 1f, 0f);

        var forward = Vector3.Cross(right, up);

        Assert.Equal(new Vector3(0f, 0f, 1f), forward);
        Assert.Equal(0f, Vector3.Dot(forward, right), precision: 5);
        Assert.Equal(0f, Vector3.Dot(forward, up), precision: 5);
    }

    [Fact]
    public void ScaleMultipliesComponentWise()
    {
        var a = new Vector3(2f, 3f, 4f);
        var b = new Vector3(5f, 6f, 7f);

        var result = Vector3.Scale(a, b);

        Assert.Equal(new Vector3(10f, 18f, 28f), result);
    }

    [Fact]
    public void ProjectOntoVector()
    {
        // Projecting (3,4,0) onto x-axis should give (3,0,0)
        var v = new Vector3(3f, 4f, 0f);
        var onNormal = new Vector3(1f, 0f, 0f);

        var result = Vector3.Project(v, onNormal);

        Assert.Equal(3f, result.x, precision: 5);
        Assert.Equal(0f, result.y, precision: 5);
        Assert.Equal(0f, result.z, precision: 5);
    }

    [Fact]
    public void ProjectOntoZeroNormalReturnsZero()
    {
        var v = new Vector3(1f, 2f, 3f);
        var result = Vector3.Project(v, Vector3.zero);

        Assert.Equal(Vector3.zero, result);
    }

    [Fact]
    public void ProjectOnPlane()
    {
        // Vector (1,1,0) projected onto XZ plane (normal = up) should give (1,0,0)
        var v = new Vector3(1f, 1f, 0f);
        var planeNormal = new Vector3(0f, 1f, 0f);

        var result = Vector3.ProjectOnPlane(v, planeNormal);

        Assert.Equal(1f, result.x, precision: 5);
        Assert.Equal(0f, result.y, precision: 5);
        Assert.Equal(0f, result.z, precision: 5);
    }

    [Fact]
    public void AngleBetweenVectorsIsCorrect()
    {
        var v1 = new Vector3(1f, 0f, 0f);
        var v2 = new Vector3(0f, 1f, 0f);

        float angle = Vector3.Angle(v1, v2);

        Assert.Equal(90f, angle, precision: 3);
    }

    [Fact]
    public void AngleBetweenParallelVectorsIsZero()
    {
        var v1 = new Vector3(1f, 0f, 0f);
        var v2 = new Vector3(2f, 0f, 0f);

        Assert.Equal(0f, Vector3.Angle(v1, v2), precision: 3);
    }

    [Fact]
    public void ClampMagnitudeScalesDown()
    {
        var v = new Vector3(3f, 4f, 0f); // magnitude=5
        var clamped = Vector3.ClampMagnitude(v, 2f);

        Assert.Equal(2f, clamped.magnitude, precision: 5);
    }

    [Fact]
    public void ClampMagnitudeDoesNotScaleUp()
    {
        var v = new Vector3(1f, 0f, 0f); // magnitude=1
        var clamped = Vector3.ClampMagnitude(v, 5f);

        Assert.Equal(v, clamped);
    }

    [Fact]
    public void ReflectVectorAcrossNormal()
    {
        // Reflecting (1,-1,0) over up normal (0,1,0) should give (1,1,0)
        var inDir = new Vector3(1f, -1f, 0f);
        var normal = new Vector3(0f, 1f, 0f);

        var result = Vector3.Reflect(inDir, normal);

        Assert.Equal(1f, result.x, precision: 5);
        Assert.Equal(1f, result.y, precision: 5);
        Assert.Equal(0f, result.z, precision: 5);
    }

    [Fact]
    public void MoveTowardsReachesTarget()
    {
        var current = new Vector3(0f, 0f, 0f);
        var target = new Vector3(3f, 4f, 0f); // distance=5
        var result = Vector3.MoveTowards(current, target, 10f);

        Assert.Equal(target, result);
    }

    [Fact]
    public void MoveTowardsMovesPartially()
    {
        var current = new Vector3(0f, 0f, 0f);
        var target = new Vector3(10f, 0f, 0f);
        var result = Vector3.MoveTowards(current, target, 3f);

        Assert.Equal(3f, result.x, precision: 5);
    }

    [Fact]
    public void NormalizeStaticMethodWorks()
    {
        var v = new Vector3(0f, 5f, 0f);
        var normalized = Vector3.Normalize(v);

        Assert.Equal(new Vector3(0f, 1f, 0f), normalized);
    }

    [Fact]
    public void NormalizeZeroVectorReturnsZero()
    {
        var result = Vector3.Normalize(Vector3.zero);
        Assert.Equal(Vector3.zero, result);
    }

    [Fact]
    public void SetMutatesComponents()
    {
        var v = new Vector3(1f, 2f, 3f);
        v.Set(7f, 8f, 9f);

        Assert.Equal(7f, v.x);
        Assert.Equal(8f, v.y);
        Assert.Equal(9f, v.z);
    }

    [Fact]
    public void IndexerReadsComponents()
    {
        var v = new Vector3(1f, 2f, 3f);

        Assert.Equal(1f, v[0]);
        Assert.Equal(2f, v[1]);
        Assert.Equal(3f, v[2]);
    }

    [Fact]
    public void IndexerThrowsOnOutOfRange()
    {
        var v = new Vector3(1f, 2f, 3f);
        Assert.Throws<IndexOutOfRangeException>(() => v[3]);
    }

    [Fact]
    public void TwoArgConstructorSetsZToZero()
    {
        var v = new Vector3(5f, 6f);
        Assert.Equal(0f, v.z);
    }

    [Fact]
    public void OrthoNormalizeProducesPerpendicularUnitVectors()
    {
        var normal = new Vector3(1f, 1f, 0f);
        var tangent = new Vector3(0f, 1f, 0f);

        Vector3.OrthoNormalize(ref normal, ref tangent);

        Assert.Equal(1f, normal.magnitude, precision: 5);
        Assert.Equal(1f, tangent.magnitude, precision: 5);
        Assert.Equal(0f, Vector3.Dot(normal, tangent), precision: 5);
    }
}
