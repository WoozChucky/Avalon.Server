using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Avalon.Common.Mathematics;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct MathfInternal
{
  public static volatile float FloatMinNormal = 1.1754944E-38f;
  public static volatile float FloatMinDenormal = float.Epsilon;
  public static bool IsFlushToZeroEnabled = (double) MathfInternal.FloatMinDenormal == 0.0;
}

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct Mathf
{
    /// <summary>
    ///   <para>The well-known 3.14159265358979... value (Read Only).</para>
    /// </summary>
    public const float PI = 3.1415927f;
    /// <summary>
    ///   <para>A representation of positive infinity (Read Only).</para>
    /// </summary>
    public const float Infinity = float.PositiveInfinity;
    /// <summary>
    ///   <para>A representation of negative infinity (Read Only).</para>
    /// </summary>
    public const float NegativeInfinity = float.NegativeInfinity;
    /// <summary>
    ///   <para>Degrees-to-radians conversion constant (Read Only).</para>
    /// </summary>
    public const float Deg2Rad = 0.017453292f;
    /// <summary>
    ///   <para>Radians-to-degrees conversion constant (Read Only).</para>
    /// </summary>
    public const float Rad2Deg = 57.29578f;
    internal const int kMaxDecimals = 15;
    /// <summary>
    ///   <para>A tiny floating point value (Read Only).</para>
    /// </summary>
    public static readonly float Epsilon = MathfInternal.IsFlushToZeroEnabled ? MathfInternal.FloatMinNormal : MathfInternal.FloatMinDenormal;

    /// <summary>
    ///   <para>Returns the sine of angle f.</para>
    /// </summary>
    /// <param name="f">The input angle, in radians.</param>
    /// <returns>
    ///   <para>The return value between -1 and +1.</para>
    /// </returns>
    public static float Sin(float f) => (float) Math.Sin((double) f);

    /// <summary>
    ///   <para>Returns the cosine of angle f.</para>
    /// </summary>
    /// <param name="f">The input angle, in radians.</param>
    /// <returns>
    ///   <para>The return value between -1 and 1.</para>
    /// </returns>
    public static float Cos(float f) => (float) Math.Cos((double) f);

    /// <summary>
    ///   <para>Returns the tangent of angle f in radians.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Tan(float f) => (float) Math.Tan((double) f);

    /// <summary>
    ///   <para>Returns the arc-sine of f - the angle in radians whose sine is f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Asin(float f) => (float) Math.Asin((double) f);

    /// <summary>
    ///   <para>Returns the arc-cosine of f - the angle in radians whose cosine is f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Acos(float f) => (float) Math.Acos((double) f);

    /// <summary>
    ///   <para>Returns the arc-tangent of f - the angle in radians whose tangent is f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Atan(float f) => (float) Math.Atan((double) f);

    /// <summary>
    ///   <para>Returns the angle in radians whose Tan is y/x.</para>
    /// </summary>
    /// <param name="y"></param>
    /// <param name="x"></param>
    public static float Atan2(float y, float x) => (float) Math.Atan2((double) y, (double) x);

    /// <summary>
    ///   <para>Returns square root of f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Sqrt(float f) => (float) Math.Sqrt((double) f);

    /// <summary>
    ///   <para>Returns the absolute value of f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Abs(float f) => Math.Abs(f);

    /// <summary>
    ///   <para>Returns the absolute value of value.</para>
    /// </summary>
    /// <param name="value"></param>
    public static int Abs(int value) => Math.Abs(value);

    /// <summary>
    ///   <para>Returns the smallest of two or more values.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static float Min(float a, float b) => (double) a < (double) b ? a : b;

    /// <summary>
    ///   <para>Returns the smallest of two or more values.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static float Min(params float[] values)
    {
      int length = values.Length;
      if (length == 0)
        return 0.0f;
      float num = values[0];
      for (int index = 1; index < length; ++index)
      {
        if ((double) values[index] < (double) num)
          num = values[index];
      }
      return num;
    }

    /// <summary>
    ///   <para>Returns the smallest of two or more values.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static int Min(int a, int b) => a < b ? a : b;

    /// <summary>
    ///   <para>Returns the smallest of two or more values.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static int Min(params int[] values)
    {
      int length = values.Length;
      if (length == 0)
        return 0;
      int num = values[0];
      for (int index = 1; index < length; ++index)
      {
        if (values[index] < num)
          num = values[index];
      }
      return num;
    }

    /// <summary>
    ///   <para>Returns the largest of two or more values. When comparing negative values, values closer to zero are considered larger.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static float Max(float a, float b) => (double) a > (double) b ? a : b;

    /// <summary>
    ///   <para>Returns the largest of two or more values. When comparing negative values, values closer to zero are considered larger.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static float Max(params float[] values)
    {
      int length = values.Length;
      if (length == 0)
        return 0.0f;
      float num = values[0];
      for (int index = 1; index < length; ++index)
      {
        if ((double) values[index] > (double) num)
          num = values[index];
      }
      return num;
    }

    /// <summary>
    ///   <para>Returns the largest value. When comparing negative values, values closer to zero are considered larger.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static int Max(int a, int b) => a > b ? a : b;

    /// <summary>
    ///   <para>Returns the largest value. When comparing negative values, values closer to zero are considered larger.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="values"></param>
    public static int Max(params int[] values)
    {
      int length = values.Length;
      if (length == 0)
        return 0;
      int num = values[0];
      for (int index = 1; index < length; ++index)
      {
        if (values[index] > num)
          num = values[index];
      }
      return num;
    }

    /// <summary>
    ///   <para>Returns f raised to power p.</para>
    /// </summary>
    /// <param name="f"></param>
    /// <param name="p"></param>
    public static float Pow(float f, float p) => (float) Math.Pow((double) f, (double) p);

    /// <summary>
    ///   <para>Returns e raised to the specified power.</para>
    /// </summary>
    /// <param name="power"></param>
    public static float Exp(float power) => (float) Math.Exp((double) power);

    /// <summary>
    ///   <para>Returns the logarithm of a specified number in a specified base.</para>
    /// </summary>
    /// <param name="f"></param>
    /// <param name="p"></param>
    public static float Log(float f, float p) => (float) Math.Log((double) f, (double) p);

    /// <summary>
    ///   <para>Returns the natural (base e) logarithm of a specified number.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Log(float f) => (float) Math.Log((double) f);

    /// <summary>
    ///   <para>Returns the base 10 logarithm of a specified number.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Log10(float f) => (float) Math.Log10((double) f);

    /// <summary>
    ///   <para>Returns the smallest integer greater than or equal to f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Ceil(float f) => (float) Math.Ceiling((double) f);

    /// <summary>
    ///   <para>Returns the largest integer smaller than or equal to f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Floor(float f) => (float) Math.Floor((double) f);

    /// <summary>
    ///   <para>Returns f rounded to the nearest integer.</para>
    /// </summary>
    /// <param name="f"></param>
    public static float Round(float f) => (float) Math.Round((double) f);

    /// <summary>
    ///   <para>Returns the smallest integer greater to or equal to f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static int CeilToInt(float f) => (int) Math.Ceiling((double) f);

    /// <summary>
    ///   <para>Returns the largest integer smaller to or equal to f.</para>
    /// </summary>
    /// <param name="f"></param>
    public static int FloorToInt(float f) => (int) Math.Floor((double) f);

    /// <summary>
    ///   <para>Returns f rounded to the nearest integer.</para>
    /// </summary>
    /// <param name="f"></param>
    public static int RoundToInt(float f) => (int) Math.Round((double) f);

    /// <summary>
    ///   <para>Returns the sign of f.</para>
    /// </summary>
    /// <param name="f"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sign(float f) => (double) f >= 0.0 ? 1f : -1f;

    /// <summary>
    ///   <para>Clamps the given value between the given minimum float and maximum float values.  Returns the given value if it is within the minimum and maximum range.</para>
    /// </summary>
    /// <param name="value">The floating point value to restrict inside the range defined by the minimum and maximum values.</param>
    /// <param name="min">The minimum floating point value to compare against.</param>
    /// <param name="max">The maximum floating point value to compare against.</param>
    /// <returns>
    ///   <para>The float result between the minimum and maximum values.</para>
    /// </returns>
    public static float Clamp(float value, float min, float max)
    {
      if ((double) value < (double) min)
        value = min;
      else if ((double) value > (double) max)
        value = max;
      return value;
    }

    /// <summary>
    ///   <para>Clamps the given value between a range defined by the given minimum integer and maximum integer values. Returns the given value if it is within min and max.</para>
    /// </summary>
    /// <param name="value">The integer point value to restrict inside the min-to-max range.</param>
    /// <param name="min">The minimum integer point value to compare against.</param>
    /// <param name="max">The maximum  integer point value to compare against.</param>
    /// <returns>
    ///   <para>The int result between min and max values.</para>
    /// </returns>
    public static int Clamp(int value, int min, int max)
    {
      if (value < min)
        value = min;
      else if (value > max)
        value = max;
      return value;
    }

    /// <summary>
    ///   <para>Clamps value between 0 and 1 and returns value.</para>
    /// </summary>
    /// <param name="value"></param>
    public static float Clamp01(float value)
    {
      if ((double) value < 0.0)
        return 0.0f;
      return (double) value > 1.0 ? 1f : value;
    }

    /// <summary>
    ///   <para>Linearly interpolates between a and b by t.</para>
    /// </summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="t">The interpolation value between the two floats.</param>
    /// <returns>
    ///   <para>The interpolated float result between the two float values.</para>
    /// </returns>
    public static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);

    /// <summary>
    ///   <para>Linearly interpolates between a and b by t with no limit to t.</para>
    /// </summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="t">The interpolation between the two floats.</param>
    /// <returns>
    ///   <para>The float value as a result from the linear interpolation.</para>
    /// </returns>
    public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    ///   <para>Same as Lerp but makes sure the values interpolate correctly when they wrap around 360 degrees.</para>
    /// </summary>
    /// <param name="a">The start angle. A float expressed in degrees.</param>
    /// <param name="b">The end angle. A float expressed in degrees.</param>
    /// <param name="t">The interpolation value between the start and end angles. This value is clamped to the range [0, 1].</param>
    /// <returns>
    ///   <para>Returns the interpolated float result between angle a and angle b, based on the interpolation value t.</para>
    /// </returns>
    public static float LerpAngle(float a, float b, float t)
    {
      float num = Mathf.Repeat(b - a, 360f);
      if ((double) num > 180.0)
        num -= 360f;
      return a + num * Mathf.Clamp01(t);
    }

    /// <summary>
    ///   <para>Moves a value current towards target.</para>
    /// </summary>
    /// <param name="current">The current value.</param>
    /// <param name="target">The value to move towards.</param>
    /// <param name="maxDelta">The maximum change applied to the current value.</param>
    public static float MoveTowards(float current, float target, float maxDelta)
    {
      return (double) Mathf.Abs(target - current) <= (double) maxDelta ? target : current + Mathf.Sign(target - current) * maxDelta;
    }

    /// <summary>
    ///   <para>Same as MoveTowards but makes sure the values interpolate correctly when they wrap around 360 degrees.</para>
    /// </summary>
    /// <param name="current"></param>
    /// <param name="target"></param>
    /// <param name="maxDelta"></param>
    public static float MoveTowardsAngle(float current, float target, float maxDelta)
    {
      float num = Mathf.DeltaAngle(current, target);
      if (-(double) maxDelta < (double) num && (double) num < (double) maxDelta)
        return target;
      target = current + num;
      return Mathf.MoveTowards(current, target, maxDelta);
    }

    /// <summary>
    ///   <para>Interpolates between from and to with smoothing at the limits.</para>
    /// </summary>
    /// <param name="from">The start of the range.</param>
    /// <param name="to">The end of the range.</param>
    /// <param name="t">The interpolation value between the from and to range limits.</param>
    /// <returns>
    ///   <para>The interpolated float result between from and to.</para>
    /// </returns>
    public static float SmoothStep(float from, float to, float t)
    {
      t = Mathf.Clamp01(t);
      t = (float) (-2.0 * (double) t * (double) t * (double) t + 3.0 * (double) t * (double) t);
      return (float) ((double) to * (double) t + (double) from * (1.0 - (double) t));
    }

    public static float Gamma(float value, float absmax, float gamma)
    {
      bool flag = (double) value < 0.0;
      float num1 = Mathf.Abs(value);
      if ((double) num1 > (double) absmax)
        return flag ? -num1 : num1;
      float num2 = Mathf.Pow(num1 / absmax, gamma) * absmax;
      return flag ? -num2 : num2;
    }

    /// <summary>
    ///   <para>Compares two floating point values and returns true if they are similar.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    public static bool Approximately(float a, float b)
    {
      return (double) Mathf.Abs(b - a) < (double) Mathf.Max(1E-06f * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)), Mathf.Epsilon * 8f);
    }

    public static float SmoothDamp(
      float current,
      float target,
      ref float currentVelocity,
      float smoothTime,
      [DefaultValue("Mathf.Infinity")] float maxSpeed,
      [DefaultValue("Time.deltaTime")] float deltaTime)
    {
      smoothTime = Mathf.Max(0.0001f, smoothTime);
      float num1 = 2f / smoothTime;
      float num2 = num1 * deltaTime;
      float num3 = (float) (1.0 / (1.0 + (double) num2 + 0.47999998927116394 * (double) num2 * (double) num2 + 0.23499999940395355 * (double) num2 * (double) num2 * (double) num2));
      float num4 = current - target;
      float num5 = target;
      float max = maxSpeed * smoothTime;
      float num6 = Mathf.Clamp(num4, -max, max);
      target = current - num6;
      float num7 = (currentVelocity + num1 * num6) * deltaTime;
      currentVelocity = (currentVelocity - num1 * num7) * num3;
      float num8 = target + (num6 + num7) * num3;
      if ((double) num5 - (double) current > 0.0 == (double) num8 > (double) num5)
      {
        num8 = num5;
        currentVelocity = (num8 - num5) / deltaTime;
      }
      return num8;
    }

    public static float SmoothDampAngle(
      float current,
      float target,
      ref float currentVelocity,
      float smoothTime,
      float maxSpeed,
      float deltaTime)
    {
      target = current + Mathf.DeltaAngle(current, target);
      return Mathf.SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
    }

    /// <summary>
    ///   <para>Loops the value t, so that it is never larger than length and never smaller than 0.</para>
    /// </summary>
    /// <param name="t"></param>
    /// <param name="length"></param>
    public static float Repeat(float t, float length)
    {
      return Mathf.Clamp(t - Mathf.Floor(t / length) * length, 0.0f, length);
    }

    /// <summary>
    ///   <para>PingPong returns a value that increments and decrements between zero and the length. It follows the triangle wave formula where the bottom is set to zero and the peak is set to length.</para>
    /// </summary>
    /// <param name="t"></param>
    /// <param name="length"></param>
    public static float PingPong(float t, float length)
    {
      t = Mathf.Repeat(t, length * 2f);
      return length - Mathf.Abs(t - length);
    }

    /// <summary>
    ///   <para>Determines where a value lies between two points.</para>
    /// </summary>
    /// <param name="a">The start of the range.</param>
    /// <param name="b">The end of the range.</param>
    /// <param name="value">The point within the range you want to calculate.</param>
    /// <returns>
    ///   <para>A value between zero and one, representing where the "value" parameter falls within the range defined by a and b.</para>
    /// </returns>
    public static float InverseLerp(float a, float b, float value)
    {
      return (double) a != (double) b ? Mathf.Clamp01((float) (((double) value - (double) a) / ((double) b - (double) a))) : 0.0f;
    }

    /// <summary>
    ///   <para>Calculates the shortest difference between two angles.</para>
    /// </summary>
    /// <param name="current">The current angle in degrees.</param>
    /// <param name="target">The target angle in degrees.</param>
    /// <returns>
    ///   <para>A value between -179 and 180, in degrees.</para>
    /// </returns>
    public static float DeltaAngle(float current, float target)
    {
      float num = Mathf.Repeat(target - current, 360f);
      if ((double) num > 180.0)
        num -= 360f;
      return num;
    }

    internal static bool LineIntersection(
      Vector2 p1,
      Vector2 p2,
      Vector2 p3,
      Vector2 p4,
      ref Vector2 result)
    {
      float num1 = p2.x - p1.x;
      float num2 = p2.y - p1.y;
      float num3 = p4.x - p3.x;
      float num4 = p4.y - p3.y;
      float num5 = (float) ((double) num1 * (double) num4 - (double) num2 * (double) num3);
      if ((double) num5 == 0.0)
        return false;
      float num6 = p3.x - p1.x;
      float num7 = p3.y - p1.y;
      float num8 = (float) ((double) num6 * (double) num4 - (double) num7 * (double) num3) / num5;
      result.x = p1.x + num8 * num1;
      result.y = p1.y + num8 * num2;
      return true;
    }

    internal static bool LineSegmentIntersection(
      Vector2 p1,
      Vector2 p2,
      Vector2 p3,
      Vector2 p4,
      ref Vector2 result)
    {
      float num1 = p2.x - p1.x;
      float num2 = p2.y - p1.y;
      float num3 = p4.x - p3.x;
      float num4 = p4.y - p3.y;
      float num5 = (float) ((double) num1 * (double) num4 - (double) num2 * (double) num3);
      if ((double) num5 == 0.0)
        return false;
      float num6 = p3.x - p1.x;
      float num7 = p3.y - p1.y;
      float num8 = (float) ((double) num6 * (double) num4 - (double) num7 * (double) num3) / num5;
      if ((double) num8 < 0.0 || (double) num8 > 1.0)
        return false;
      float num9 = (float) ((double) num6 * (double) num2 - (double) num7 * (double) num1) / num5;
      if ((double) num9 < 0.0 || (double) num9 > 1.0)
        return false;
      result.x = p1.x + num8 * num1;
      result.y = p1.y + num8 * num2;
      return true;
    }

    internal static long RandomToLong(System.Random r)
    {
      byte[] buffer = new byte[8];
      r.NextBytes(buffer);
      return (long) BitConverter.ToUInt64(buffer, 0) & long.MaxValue;
    }
    
    internal static float ClampToFloat(double value)
    {
      if (double.IsPositiveInfinity(value))
        return float.PositiveInfinity;
      if (double.IsNegativeInfinity(value))
        return float.NegativeInfinity;
      if (value < -3.4028234663852886E+38)
        return float.MinValue;
      return value > 3.4028234663852886E+38 ? float.MaxValue : (float) value;
    }
    
    internal static int ClampToInt(long value)
    {
      if (value < (long) int.MinValue)
        return int.MinValue;
      return value > (long) int.MaxValue ? int.MaxValue : (int) value;
    }
    
    internal static uint ClampToUInt(long value)
    {
      if (value < 0L)
        return 0;
      return value > (long) uint.MaxValue ? uint.MaxValue : (uint) value;
    }

    internal static float RoundToMultipleOf(float value, float roundingValue)
    {
      return (double) roundingValue == 0.0 ? value : Mathf.Round(value / roundingValue) * roundingValue;
    }

    internal static float GetClosestPowerOfTen(float positiveNumber)
    {
      return (double) positiveNumber <= 0.0 ? 1f : Mathf.Pow(10f, (float) Mathf.RoundToInt(Mathf.Log10(positiveNumber)));
    }

    internal static int GetNumberOfDecimalsForMinimumDifference(float minDifference)
    {
      return Mathf.Clamp(-Mathf.FloorToInt(Mathf.Log10(Mathf.Abs(minDifference))), 0, 15);
    }

    internal static int GetNumberOfDecimalsForMinimumDifference(double minDifference)
    {
      return (int) Math.Max(0.0, -Math.Floor(Math.Log10(Math.Abs(minDifference))));
    }
    
    internal static float RoundBasedOnMinimumDifference(float valueToRound, float minDifference)
    {
      return (double) minDifference == 0.0 ? Mathf.DiscardLeastSignificantDecimal(valueToRound) : (float) Math.Round((double) valueToRound, Mathf.GetNumberOfDecimalsForMinimumDifference(minDifference), MidpointRounding.AwayFromZero);
    }
    
    internal static double RoundBasedOnMinimumDifference(double valueToRound, double minDifference)
    {
      return minDifference == 0.0 ? Mathf.DiscardLeastSignificantDecimal(valueToRound) : Math.Round(valueToRound, Mathf.GetNumberOfDecimalsForMinimumDifference(minDifference), MidpointRounding.AwayFromZero);
    }

    internal static float DiscardLeastSignificantDecimal(float v)
    {
      int digits = Mathf.Clamp((int) (5.0 - (double) Mathf.Log10(Mathf.Abs(v))), 0, 15);
      return (float) Math.Round((double) v, digits, MidpointRounding.AwayFromZero);
    }

    internal static double DiscardLeastSignificantDecimal(double v)
    {
      int digits = Math.Max(0, (int) (5.0 - Math.Log10(Math.Abs(v))));
      try
      {
        return Math.Round(v, digits);
      }
      catch (ArgumentOutOfRangeException)
      {
        return 0.0;
      }
    }

    /// <summary>
    ///   <para>Returns the next power of two that is equal to, or greater than, the argument.</para>
    /// </summary>
    /// <param name="value"></param>
    public static int NextPowerOfTwo(int value)
    {
      --value;
      value |= value >> 16;
      value |= value >> 8;
      value |= value >> 4;
      value |= value >> 2;
      value |= value >> 1;
      return value + 1;
    }

    /// <summary>
    ///   <para>Returns the closest power of two value.</para>
    /// </summary>
    /// <param name="value"></param>
    public static int ClosestPowerOfTwo(int value)
    {
      int num1 = Mathf.NextPowerOfTwo(value);
      int num2 = num1 >> 1;
      return value - num2 < num1 - value ? num2 : num1;
    }

    /// <summary>
    ///   <para>Returns true if the value is power of two.</para>
    /// </summary>
    /// <param name="value"></param>
    public static bool IsPowerOfTwo(int value) => (value & value - 1) == 0;

    [MethodImpl(MethodImplOptions.InternalCall)]
    private static extern void CorrelatedColorTemperatureToRGB_Injected(float kelvin, out Color ret);
}
