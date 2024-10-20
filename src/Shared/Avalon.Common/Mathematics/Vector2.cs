using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Avalon.Common.Mathematics;

public struct Vector2
{
    /// <summary>
    ///     <para>X component of the vector.</para>
    /// </summary>
    public float x;

    /// <summary>
    ///     <para>Y component of the vector.</para>
    /// </summary>
    public float y;

    public const float KEpsilon = 1E-05f;
    public const float KEpsilonNormalSqrt = 1E-15f;

    public float this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            switch (index)
            {
                case 0:
                    return x;
                case 1:
                    return y;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector2 index!");
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            switch (index)
            {
                case 0:
                    x = value;
                    break;
                case 1:
                    y = value;
                    break;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector2 index!");
            }
        }
    }

    /// <summary>
    ///     <para>Constructs a new vector with given x, y components.</para>
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    /// <summary>
    ///     <para>Set x and y components of an existing Vector2.</para>
    /// </summary>
    /// <param name="newX"></param>
    /// <param name="newY"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(float newX, float newY)
    {
        x = newX;
        y = newY;
    }

    /// <summary>
    ///     <para>Linearly interpolates between vectors a and b by t.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="t"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        t = Mathf.Clamp01(t);
        return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
    }

    /// <summary>
    ///     <para>Linearly interpolates between vectors a and b by t.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="t"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) =>
        new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

    /// <summary>
    ///     <para>Moves a point current towards target.</para>
    /// </summary>
    /// <param name="current"></param>
    /// <param name="target"></param>
    /// <param name="maxDistanceDelta"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
    {
        float num1 = target.x - current.x;
        float num2 = target.y - current.y;
        float d = (float)(num1 * (double)num1 + num2 * (double)num2);
        if (d == 0.0 || (maxDistanceDelta >= 0.0 && d <= maxDistanceDelta * (double)maxDistanceDelta))
        {
            return target;
        }

        float num3 = (float)Math.Sqrt(d);
        return new Vector2(current.x + num1 / num3 * maxDistanceDelta, current.y + num2 / num3 * maxDistanceDelta);
    }

    /// <summary>
    ///     <para>Multiplies two vectors component-wise.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Scale(Vector2 a, Vector2 b) => new(a.x * b.x, a.y * b.y);

    /// <summary>
    ///     <para>Multiplies every component of this vector by the same component of scale.</para>
    /// </summary>
    /// <param name="scale"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Scale(Vector2 scale)
    {
        x *= scale.x;
        y *= scale.y;
    }

    /// <summary>
    ///     <para>Makes this vector have a magnitude of 1.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Normalize()
    {
        float magnitude = this.magnitude;
        if (magnitude > 9.999999747378752E-06)
        {
            this = this / magnitude;
        }
        else
        {
            this = zero;
        }
    }

    /// <summary>
    ///     <para>
    ///         Returns a normalized vector based on the current vector. The normalized vector has a magnitude of 1 and is in
    ///         the same direction as the current vector. Returns a zero vector If the current vector is too small to be
    ///         normalized.
    ///     </para>
    /// </summary>
    public Vector2 normalized
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Vector2 normalized = new(x, y);
            normalized.Normalize();
            return normalized;
        }
    }

    /// <summary>
    ///     <para>Returns a formatted string for this vector.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => ToString(null, null);

    /// <summary>
    ///     <para>Returns a formatted string for this vector.</para>
    /// </summary>
    /// <param name="format">A numeric format string.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(string format) => ToString(format, null);

    /// <summary>
    ///     <para>Returns a formatted string for this vector.</para>
    /// </summary>
    /// <param name="format">A numeric format string.</param>
    /// <param name="formatProvider">An object that specifies culture-specific formatting.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(string format, IFormatProvider formatProvider)
    {
        if (string.IsNullOrEmpty(format))
        {
            format = "F2";
        }

        if (formatProvider == null)
        {
            formatProvider = CultureInfo.InvariantCulture.NumberFormat;
        }

        return AvalonString.Format("({0}, {1})", x.ToString(format, formatProvider),
            y.ToString(format, formatProvider));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);

    /// <summary>
    ///     <para>Returns true if the given vector is exactly equal to this vector.</para>
    /// </summary>
    /// <param name="other"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object other) => other is Vector2 other1 && Equals(other1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Vector2 other) => x == (double)other.x && y == (double)other.y;

    /// <summary>
    ///     <para>Reflects a vector off the surface defined by a normal.</para>
    /// </summary>
    /// <param name="inDirection">The direction vector towards the surface.</param>
    /// <param name="inNormal">The normal vector that defines the surface.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)
    {
        float num = -2f * Dot(inNormal, inDirection);
        return new Vector2(num * inNormal.x + inDirection.x, num * inNormal.y + inDirection.y);
    }

    /// <summary>
    ///     <para>
    ///         Returns the 2D vector perpendicular to this 2D vector. The result is always rotated 90-degrees in a
    ///         counter-clockwise direction for a 2D coordinate system where the positive Y axis goes up.
    ///     </para>
    /// </summary>
    /// <param name="inDirection">The input direction.</param>
    /// <returns>
    ///     <para>The perpendicular direction.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Perpendicular(Vector2 inDirection) => new(-inDirection.y, inDirection.x);

    /// <summary>
    ///     <para>Dot Product of two vectors.</para>
    /// </summary>
    /// <param name="lhs"></param>
    /// <param name="rhs"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector2 lhs, Vector2 rhs) => (float)(lhs.x * (double)rhs.x + lhs.y * (double)rhs.y);

    /// <summary>
    ///     <para>Returns the length of this vector (Read Only).</para>
    /// </summary>
    public float magnitude
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (float)Math.Sqrt(x * (double)x + y * (double)y);
    }

    /// <summary>
    ///     <para>Returns the squared length of this vector (Read Only).</para>
    /// </summary>
    public float sqrMagnitude
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (float)(x * (double)x + y * (double)y);
    }

    /// <summary>
    ///     <para>Gets the unsigned angle in degrees between from and to.</para>
    /// </summary>
    /// <param name="from">The vector from which the angular difference is measured.</param>
    /// <param name="to">The vector to which the angular difference is measured.</param>
    /// <returns>
    ///     <para>The unsigned angle in degrees between the two vectors.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Angle(Vector2 from, Vector2 to)
    {
        float num = (float)Math.Sqrt(from.sqrMagnitude * (double)to.sqrMagnitude);
        return num < 1.0000000036274937E-15
            ? 0.0f
            : (float)Math.Acos(Mathf.Clamp(Dot(from, to) / num, -1f, 1f)) * 57.29578f;
    }

    /// <summary>
    ///     <para>Gets the signed angle in degrees between from and to.</para>
    /// </summary>
    /// <param name="from">The vector from which the angular difference is measured.</param>
    /// <param name="to">The vector to which the angular difference is measured.</param>
    /// <returns>
    ///     <para>The signed angle in degrees between the two vectors.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SignedAngle(Vector2 from, Vector2 to) =>
        Angle(from, to) * Mathf.Sign((float)(from.x * (double)to.y - from.y * (double)to.x));

    /// <summary>
    ///     <para>Returns the distance between a and b.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector2 a, Vector2 b)
    {
        float num1 = a.x - b.x;
        float num2 = a.y - b.y;
        return (float)Math.Sqrt(num1 * (double)num1 + num2 * (double)num2);
    }

    /// <summary>
    ///     <para>Returns a copy of vector with its magnitude clamped to maxLength.</para>
    /// </summary>
    /// <param name="vector"></param>
    /// <param name="maxLength"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
    {
        float sqrMagnitude = vector.sqrMagnitude;
        if (sqrMagnitude <= maxLength * (double)maxLength)
        {
            return vector;
        }

        float num1 = (float)Math.Sqrt(sqrMagnitude);
        float num2 = vector.x / num1;
        float num3 = vector.y / num1;
        return new Vector2(num2 * maxLength, num3 * maxLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SqrMagnitude(Vector2 a) => (float)(a.x * (double)a.x + a.y * (double)a.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float SqrMagnitude() => (float)(x * (double)x + y * (double)y);

    /// <summary>
    ///     <para>Returns a vector that is made from the smallest components of two vectors.</para>
    /// </summary>
    /// <param name="lhs"></param>
    /// <param name="rhs"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Min(Vector2 lhs, Vector2 rhs) => new(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y));

    /// <summary>
    ///     <para>Returns a vector that is made from the largest components of two vectors.</para>
    /// </summary>
    /// <param name="lhs"></param>
    /// <param name="rhs"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Max(Vector2 lhs, Vector2 rhs) => new(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y));

    public static Vector2 SmoothDamp(
        Vector2 current,
        Vector2 target,
        ref Vector2 currentVelocity,
        float smoothTime,
        [DefaultValue("Mathf.Infinity")] float maxSpeed,
        [DefaultValue("Time.deltaTime")] float deltaTime)
    {
        smoothTime = Mathf.Max(0.0001f, smoothTime);
        float num1 = 2f / smoothTime;
        float num2 = num1 * deltaTime;
        float num3 = (float)(1.0 / (1.0 + num2 + 0.47999998927116394 * num2 * num2 +
                                    0.23499999940395355 * num2 * num2 * num2));
        float num4 = current.x - target.x;
        float num5 = current.y - target.y;
        Vector2 vector2 = target;
        float num6 = maxSpeed * smoothTime;
        float num7 = num6 * num6;
        float d = (float)(num4 * (double)num4 + num5 * (double)num5);
        if (d > (double)num7)
        {
            float num8 = (float)Math.Sqrt(d);
            num4 = num4 / num8 * num6;
            num5 = num5 / num8 * num6;
        }

        target.x = current.x - num4;
        target.y = current.y - num5;
        float num9 = (currentVelocity.x + num1 * num4) * deltaTime;
        float num10 = (currentVelocity.y + num1 * num5) * deltaTime;
        currentVelocity.x = (currentVelocity.x - num1 * num9) * num3;
        currentVelocity.y = (currentVelocity.y - num1 * num10) * num3;
        float x = target.x + (num4 + num9) * num3;
        float y = target.y + (num5 + num10) * num3;
        float num11 = vector2.x - current.x;
        float num12 = vector2.y - current.y;
        float num13 = x - vector2.x;
        float num14 = y - vector2.y;
        if (num11 * (double)num13 + num12 * (double)num14 > 0.0)
        {
            x = vector2.x;
            y = vector2.y;
            currentVelocity.x = (x - vector2.x) / deltaTime;
            currentVelocity.y = (y - vector2.y) / deltaTime;
        }

        return new Vector2(x, y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.x * b.x, a.y * b.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.x / b.x, a.y / b.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Vector2 a, float d) => new(a.x * d, a.y * d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(float d, Vector2 a) => new(a.x * d, a.y * d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator /(Vector2 a, float d) => new(a.x / d, a.y / d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector2 lhs, Vector2 rhs)
    {
        float num1 = lhs.x - rhs.x;
        float num2 = lhs.y - rhs.y;
        return num1 * (double)num1 + num2 * (double)num2 < 9.999999439624929E-11;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector2 lhs, Vector2 rhs) => !(lhs == rhs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(Vector3 v) => new(v.x, v.y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(Vector2 v) => new(v.x, v.y, 0.0f);

    // Implicit conversions
    public static implicit operator Vector2(System.Numerics.Vector2 numVec) => new Vector3(numVec.X, numVec.Y);

    public static implicit operator System.Numerics.Vector2(Vector2 customVec) => new(customVec.x, customVec.y);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(0, 0).</para>
    /// </summary>
    public static Vector2 zero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(1, 1).</para>
    /// </summary>
    public static Vector2 one
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(1f, 1f);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(0, 1).</para>
    /// </summary>
    public static Vector2 up
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, 1f);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(0, -1).</para>
    /// </summary>
    public static Vector2 down
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, -1f);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(-1, 0).</para>
    /// </summary>
    public static Vector2 left
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(-1f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(1, 0).</para>
    /// </summary>
    public static Vector2 right
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(1f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(float.PositiveInfinity, float.PositiveInfinity).</para>
    /// </summary>
    public static Vector2 positiveInfinity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>
    ///     <para>Shorthand for writing Vector2(float.NegativeInfinity, float.NegativeInfinity).</para>
    /// </summary>
    public static Vector2 negativeInfinity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(float.NegativeInfinity, float.NegativeInfinity);
}
