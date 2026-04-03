using Avalon.Common.Mathematics;
using Xunit;

namespace Avalon.Shared.UnitTests.Mathematics;

public class MathfShould
{
    // ── Trigonometry ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.5707964f, 1f)]  // π/2
    public void ReturnCorrectSin(float radians, float expected)
    {
        Assert.Equal(expected, Mathf.Sin(radians), precision: 5);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1.5707964f, 0f)]  // π/2
    public void ReturnCorrectCos(float radians, float expected)
    {
        Assert.Equal(expected, Mathf.Cos(radians), precision: 5);
    }

    [Fact]
    public void ReturnCorrectTan()
    {
        Assert.Equal(0f, Mathf.Tan(0f), precision: 5);
        Assert.Equal(1f, Mathf.Tan(Mathf.PI / 4f), precision: 5);
    }

    [Fact]
    public void ReturnCorrectAsin()
    {
        Assert.Equal(0f, Mathf.Asin(0f), precision: 5);
        Assert.Equal(Mathf.PI / 2f, Mathf.Asin(1f), precision: 5);
    }

    [Fact]
    public void ReturnCorrectAcos()
    {
        Assert.Equal(Mathf.PI / 2f, Mathf.Acos(0f), precision: 5);
        Assert.Equal(0f, Mathf.Acos(1f), precision: 5);
    }

    [Fact]
    public void ReturnCorrectAtan()
    {
        Assert.Equal(0f, Mathf.Atan(0f), precision: 5);
        Assert.Equal(Mathf.PI / 4f, Mathf.Atan(1f), precision: 5);
    }

    [Fact]
    public void ReturnCorrectAtan2()
    {
        Assert.Equal(0f, Mathf.Atan2(0f, 1f), precision: 5);
        Assert.Equal(Mathf.PI / 2f, Mathf.Atan2(1f, 0f), precision: 5);
    }

    // ── Roots / Powers ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(4f, 2f)]
    [InlineData(9f, 3f)]
    public void ReturnCorrectSqrt(float input, float expected)
    {
        Assert.Equal(expected, Mathf.Sqrt(input), precision: 5);
    }

    [Fact]
    public void ReturnCorrectPow()
    {
        Assert.Equal(8f, Mathf.Pow(2f, 3f), precision: 5);
        Assert.Equal(1f, Mathf.Pow(5f, 0f), precision: 5);
    }

    [Fact]
    public void ReturnCorrectExp()
    {
        Assert.Equal(1f, Mathf.Exp(0f), precision: 5);
        Assert.Equal((float)Math.E, Mathf.Exp(1f), precision: 5);
    }

    [Fact]
    public void ReturnCorrectLog()
    {
        Assert.Equal(0f, Mathf.Log(1f), precision: 5);
        Assert.Equal(3f, Mathf.Log(1000f, 10f), precision: 2);
    }

    [Fact]
    public void ReturnCorrectLog10()
    {
        Assert.Equal(0f, Mathf.Log10(1f), precision: 5);
        Assert.Equal(2f, Mathf.Log10(100f), precision: 5);
    }

    // ── Rounding ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.4f, 1f)]
    [InlineData(1.5f, 2f)]
    [InlineData(-1.4f, -1f)]
    public void ReturnCorrectRound(float input, float expected)
    {
        Assert.Equal(expected, Mathf.Round(input), precision: 5);
    }

    [Theory]
    [InlineData(1.1f, 2)]
    [InlineData(1.0f, 1)]
    [InlineData(-1.1f, -1)]
    public void ReturnCorrectCeilToInt(float input, int expected)
    {
        Assert.Equal(expected, Mathf.CeilToInt(input));
    }

    [Theory]
    [InlineData(1.9f, 1)]
    [InlineData(1.0f, 1)]
    [InlineData(-1.9f, -2)]
    public void ReturnCorrectFloorToInt(float input, int expected)
    {
        Assert.Equal(expected, Mathf.FloorToInt(input));
    }

    [Theory]
    [InlineData(1.4f, 1)]
    [InlineData(1.5f, 2)]
    public void ReturnCorrectRoundToInt(float input, int expected)
    {
        Assert.Equal(expected, Mathf.RoundToInt(input));
    }

    [Theory]
    [InlineData(1.1f, 2f)]
    [InlineData(1.0f, 1f)]
    [InlineData(-1.1f, -1f)]
    public void ReturnCorrectCeil(float input, float expected)
    {
        Assert.Equal(expected, Mathf.Ceil(input), precision: 5);
    }

    [Theory]
    [InlineData(1.9f, 1f)]
    [InlineData(1.0f, 1f)]
    [InlineData(-1.9f, -2f)]
    public void ReturnCorrectFloor(float input, float expected)
    {
        Assert.Equal(expected, Mathf.Floor(input), precision: 5);
    }

    // ── Interpolation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0f, 10f, 0.5f, 5f)]
    [InlineData(0f, 10f, 1.5f, 10f)] // clamped
    [InlineData(0f, 10f, -1f, 0f)]   // clamped
    public void LerpClampsToRange(float a, float b, float t, float expected)
    {
        Assert.Equal(expected, Mathf.Lerp(a, b, t), precision: 5);
    }

    [Fact]
    public void LerpUnclampedExtendsRange()
    {
        Assert.Equal(20f, Mathf.LerpUnclamped(0f, 10f, 2f), precision: 5);
        Assert.Equal(-10f, Mathf.LerpUnclamped(0f, 10f, -1f), precision: 5);
    }

    [Fact]
    public void InverseLerpReturnsCorrectT()
    {
        Assert.Equal(0.5f, Mathf.InverseLerp(0f, 10f, 5f), precision: 5);
        Assert.Equal(0f, Mathf.InverseLerp(0f, 10f, 0f), precision: 5);
        Assert.Equal(1f, Mathf.InverseLerp(0f, 10f, 10f), precision: 5);
    }

    [Fact]
    public void InverseLerpReturnZeroWhenAEqualsB()
    {
        Assert.Equal(0f, Mathf.InverseLerp(5f, 5f, 5f));
    }

    [Fact]
    public void MoveTowardsReachesTarget()
    {
        Assert.Equal(10f, Mathf.MoveTowards(8f, 10f, 5f));   // reaches target
        Assert.Equal(13f, Mathf.MoveTowards(10f, 20f, 3f));  // partial step
    }

    [Fact]
    public void SmoothStepInterpolatesCorrectly()
    {
        Assert.Equal(0f, Mathf.SmoothStep(0f, 1f, 0f), precision: 5);
        Assert.Equal(1f, Mathf.SmoothStep(0f, 1f, 1f), precision: 5);
        // midpoint has S-curve value
        float mid = Mathf.SmoothStep(0f, 1f, 0.5f);
        Assert.Equal(0.5f, mid, precision: 5);
    }

    // ── Angle helpers ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0f, 90f, 90f)]
    [InlineData(350f, 10f, 20f)]   // wrap around
    [InlineData(10f, 350f, -20f)]  // negative wrap
    public void DeltaAngleReturnsShortestDifference(float current, float target, float expected)
    {
        Assert.Equal(expected, Mathf.DeltaAngle(current, target), precision: 4);
    }

    [Theory]
    [InlineData(3f, 10f, 3f)]   // within range - stays as-is
    [InlineData(12f, 10f, 2f)]  // wraps: 12 - floor(12/10)*10 = 2
    public void RepeatWrapsValue(float t, float length, float expected)
    {
        Assert.Equal(expected, Mathf.Repeat(t, length), precision: 5);
    }

    [Fact]
    public void PingPongBouncesValue()
    {
        Assert.Equal(3f, Mathf.PingPong(3f, 5f), precision: 5);   // going up
        Assert.Equal(3f, Mathf.PingPong(7f, 5f), precision: 5);   // bouncing back (10-7=3)
    }

    // ── Approximately ─────────────────────────────────────────────────────────

    [Fact]
    public void ApproximatelyReturnsTrueForNearValues()
    {
        Assert.True(Mathf.Approximately(1.0f, 1.0f));
        Assert.True(Mathf.Approximately(0.1f + 0.2f, 0.3f));
    }

    [Fact]
    public void ApproximatelyReturnsFalseForDistantValues()
    {
        Assert.False(Mathf.Approximately(1.0f, 2.0f));
    }

    // ── Multi-param Min/Max ───────────────────────────────────────────────────

    [Fact]
    public void ParamsMinFloat()
    {
        Assert.Equal(1f, Mathf.Min(3f, 1f, 2f));
        Assert.Equal(0f, Mathf.Min(new float[0]));
    }

    [Fact]
    public void ParamsMaxFloat()
    {
        Assert.Equal(3f, Mathf.Max(1f, 3f, 2f));
        Assert.Equal(0f, Mathf.Max(new float[0]));
    }

    [Fact]
    public void ParamsMinInt()
    {
        Assert.Equal(1, Mathf.Min(3, 1, 2));
        Assert.Equal(0, Mathf.Min(new int[0]));
    }

    [Fact]
    public void ParamsMaxInt()
    {
        Assert.Equal(3, Mathf.Max(1, 3, 2));
        Assert.Equal(0, Mathf.Max(new int[0]));
    }

    // ── LerpAngle ─────────────────────────────────────────────────────────────

    [Fact]
    public void LerpAngleWrapsAround360()
    {
        // Going from 350 to 10 degrees: delta = 20, at t=0.5 result = 350 + 10 = 360
        float result = Mathf.LerpAngle(350f, 10f, 0.5f);
        Assert.Equal(360f, result, precision: 3);
    }


    [Theory]
    [InlineData(10, 5, 15, 10)]
    [InlineData(0, 5, 15, 5)]
    [InlineData(20, 5, 15, 15)]
    public void ClampIntValues(int value, int min, int max, int expected)
    {
        Assert.Equal(expected, Mathf.Clamp(value, min, max));
    }

    [Theory]
    [InlineData(10f, 5f, 15f, 10f)]
    [InlineData(0f, 5f, 15f, 5f)]
    [InlineData(20f, 5f, 15f, 15f)]
    public void ClampFloatValues(float value, float min, float max, float expected)
    {
        Assert.Equal(expected, Mathf.Clamp(value, min, max));
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    public void Clamp01Values(float value, float expected)
    {
        Assert.Equal(expected, Mathf.Clamp01(value));
    }

    [Theory]
    [InlineData(0f, 10f, 0.5f, 5f)]
    [InlineData(0f, 10f, 0f, 0f)]
    [InlineData(0f, 10f, 1f, 10f)]
    public void LerpValues(float a, float b, float t, float expected)
    {
        Assert.Equal(expected, Mathf.Lerp(a, b, t));
    }

    [Fact]
    public void ReturnCorrectSign()
    {
        Assert.Equal(1f, Mathf.Sign(10f));
        Assert.Equal(1f, Mathf.Sign(0f));
        Assert.Equal(-1f, Mathf.Sign(-10f));
    }

    [Fact]
    public void ReturnCorrectAbs()
    {
        Assert.Equal(10f, Mathf.Abs(-10f));
        Assert.Equal(10, Mathf.Abs(-10));
    }

    [Fact]
    public void ReturnCorrectMinMax()
    {
        Assert.Equal(5f, Mathf.Min(5f, 10f));
        Assert.Equal(10f, Mathf.Max(5f, 10f));
        Assert.Equal(5, Mathf.Min(5, 10));
        Assert.Equal(10, Mathf.Max(5, 10));
    }
}
