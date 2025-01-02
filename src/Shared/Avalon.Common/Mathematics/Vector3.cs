using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable InconsistentNaming
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Avalon.Common.Mathematics;

public struct Vector3 : IEquatable<Vector3>, IFormattable
{
    public const float kEpsilon = 1E-05f;
    public const float kEpsilonNormalSqrt = 1E-15f;

    /// <summary>
    ///     <para>X component of the vector.</para>
    /// </summary>
    public float x;

    /// <summary>
    ///     <para>Y component of the vector.</para>
    /// </summary>
    public float y;

    /// <summary>
    ///     <para>Z component of the vector.</para>
    /// </summary>
    public float z;

    /// <summary>
    ///     <para>Spherically interpolates between two vectors.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="t"></param>
    public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        float dot = Dot(a, b);
        dot = Math.Clamp(dot, -1.0f, 1.0f);
        float theta = (float)Math.Acos(dot) * t;
        Vector3 relativeVec = b - a * dot;
        relativeVec = Normalize(relativeVec);
        return a * (float)Math.Cos(theta) + relativeVec * (float)Math.Sin(theta);
    }

    /// <summary>
    ///     <para>Spherically interpolates between two vectors.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="t"></param>
    public static Vector3 SlerpUnclamped(Vector3 a, Vector3 b, float t)
    {
        float dot = Dot(a, b);
        dot = Math.Clamp(dot, -1.0f, 1.0f);
        float theta = (float)Math.Acos(dot);
        float sinTheta = (float)Math.Sin(theta);

        if (sinTheta < 1e-6)
        {
            return Lerp(a, b, t);
        }

        float scaleA = (float)Math.Sin((1 - t) * theta) / sinTheta;
        float scaleB = (float)Math.Sin(t * theta) / sinTheta;

        return a * scaleA + b * scaleB;
    }

    public static void OrthoNormalize(ref Vector3 normal, ref Vector3 tangent)
    {
        normal = Normalize(normal);
        tangent = Normalize(tangent - Dot(tangent, normal) * normal);
    }

    public static void OrthoNormalize(
        ref Vector3 normal,
        ref Vector3 tangent,
        ref Vector3 binormal)
    {
        normal = Normalize(normal);
        tangent = Normalize(tangent - Dot(tangent, normal) * normal);
        binormal = Normalize(binormal - Dot(binormal, normal) * normal - Dot(binormal, tangent) * tangent);
    }

    /// <summary>
    ///     <para>Rotates a vector current towards target.</para>
    /// </summary>
    /// <param name="current">The vector being managed.</param>
    /// <param name="target">The vector.</param>
    /// <param name="maxRadiansDelta">The maximum angle in radians allowed for this rotation.</param>
    /// <param name="maxMagnitudeDelta">The maximum allowed change in vector magnitude for this rotation.</param>
    /// <returns>
    ///     <para>The location that RotateTowards generates.</para>
    /// </returns>
    public static Vector3 RotateTowards(
        Vector3 current,
        Vector3 target,
        float maxRadiansDelta,
        float maxMagnitudeDelta)
    {
        float currentMagnitude = current.Length();
        float targetMagnitude = target.Length();

        if (currentMagnitude == 0 || targetMagnitude == 0)
        {
            return zero;
        }

        Vector3 currentDir = current / currentMagnitude;
        Vector3 targetDir = target / targetMagnitude;

        float dot = Dot(currentDir, targetDir);
        dot = Math.Clamp(dot, -1.0f, 1.0f);

        float angle = (float)Math.Acos(dot);
        float angleDelta = Math.Min(maxRadiansDelta, angle);

        Vector3 axis = Normalize(Cross(currentDir, targetDir));
        Quaternion rotation = Quaternion.CreateFromAxisAngle(axis, angleDelta);
        Vector3 rotatedDir = Transform(currentDir, rotation);

        float newMagnitude = Math.Min(targetMagnitude, currentMagnitude + maxMagnitudeDelta);
        return rotatedDir * newMagnitude;
    }

    public static Vector3 Transform(Vector3 vector, Quaternion rotation)
    {
        float x = vector.x, y = vector.y, z = vector.z;
        float qx = rotation.X, qy = rotation.Y, qz = rotation.Z, qw = rotation.W;

        // Calculate the rotation matrix components
        float ix = qw * x + qy * z - qz * y;
        float iy = qw * y + qz * x - qx * z;
        float iz = qw * z + qx * y - qy * x;
        float iw = -qx * x - qy * y - qz * z;

        return new Vector3(
            ix * qw + iw * -qx + iy * -qz - iz * -qy,
            iy * qw + iw * -qy + iz * -qx - ix * -qz,
            iz * qw + iw * -qz + ix * -qy - iy * -qx
        );
    }

    public static Vector3 Transform(Vector3 vector, Matrix4x4 matrix)
    {
        Vector3 result = new();

        float x = vector.x;
        float y = vector.y;
        float z = vector.z;

        result.x = x * matrix.M11 + y * matrix.M21 + z * matrix.M31 + matrix.M41;
        result.y = x * matrix.M12 + y * matrix.M22 + z * matrix.M32 + matrix.M42;
        result.z = x * matrix.M13 + y * matrix.M23 + z * matrix.M33 + matrix.M43;

        return result;
    }

    public float Length() => (float)Math.Sqrt(x * x + y * y + z * z);

    /// <summary>
    ///     <para>Linearly interpolates between two points.</para>
    /// </summary>
    /// <param name="a">Start value, returned when t = 0.</param>
    /// <param name="b">End value, returned when t = 1.</param>
    /// <param name="t">Value used to interpolate between a and b.</param>
    /// <returns>
    ///     <para>Interpolated value, equals to a + (b - a) * t.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        t = Mathf.Clamp01(t);
        return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }

    /// <summary>
    ///     <para>Linearly interpolates between two vectors.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <param name="t"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) =>
        new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

    /// <summary>
    ///     <para>
    ///         Calculate a position between the points specified by current and target, moving no farther than the distance
    ///         specified by maxDistanceDelta.
    ///     </para>
    /// </summary>
    /// <param name="current">The position to move from.</param>
    /// <param name="target">The position to move towards.</param>
    /// <param name="maxDistanceDelta">Distance to move current per call.</param>
    /// <returns>
    ///     <para>The new position.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
    {
        float num1 = target.x - current.x;
        float num2 = target.y - current.y;
        float num3 = target.z - current.z;
        float d = (float)(num1 * (double)num1 + num2 * (double)num2 + num3 * (double)num3);
        if (d == 0.0 || (maxDistanceDelta >= 0.0 && d <= maxDistanceDelta * (double)maxDistanceDelta))
        {
            return target;
        }

        float num4 = (float)Math.Sqrt(d);
        return new Vector3(current.x + num1 / num4 * maxDistanceDelta, current.y + num2 / num4 * maxDistanceDelta,
            current.z + num3 / num4 * maxDistanceDelta);
    }


    public static Vector3 SmoothDamp(
        Vector3 current,
        Vector3 target,
        ref Vector3 currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime)
    {
        smoothTime = Mathf.Max(0.0001f, smoothTime);
        float num1 = 2f / smoothTime;
        float num2 = num1 * deltaTime;
        float num3 = (float)(1.0 / (1.0 + num2 + 0.47999998927116394 * num2 * num2 +
                                    0.23499999940395355 * num2 * num2 * num2));
        float num4 = current.x - target.x;
        float num5 = current.y - target.y;
        float num6 = current.z - target.z;
        Vector3 vector3 = target;
        float num7 = maxSpeed * smoothTime;
        float num8 = num7 * num7;
        float d = (float)(num4 * (double)num4 + num5 * (double)num5 + num6 * (double)num6);
        if (d > (double)num8)
        {
            float num9 = (float)Math.Sqrt(d);
            num4 = num4 / num9 * num7;
            num5 = num5 / num9 * num7;
            num6 = num6 / num9 * num7;
        }

        target.x = current.x - num4;
        target.y = current.y - num5;
        target.z = current.z - num6;
        float num10 = (currentVelocity.x + num1 * num4) * deltaTime;
        float num11 = (currentVelocity.y + num1 * num5) * deltaTime;
        float num12 = (currentVelocity.z + num1 * num6) * deltaTime;
        currentVelocity.x = (currentVelocity.x - num1 * num10) * num3;
        currentVelocity.y = (currentVelocity.y - num1 * num11) * num3;
        currentVelocity.z = (currentVelocity.z - num1 * num12) * num3;
        float x = target.x + (num4 + num10) * num3;
        float y = target.y + (num5 + num11) * num3;
        float z = target.z + (num6 + num12) * num3;
        float num13 = vector3.x - current.x;
        float num14 = vector3.y - current.y;
        float num15 = vector3.z - current.z;
        float num16 = x - vector3.x;
        float num17 = y - vector3.y;
        float num18 = z - vector3.z;
        if (num13 * (double)num16 + num14 * (double)num17 + num15 * (double)num18 > 0.0)
        {
            x = vector3.x;
            y = vector3.y;
            z = vector3.z;
            currentVelocity.x = (x - vector3.x) / deltaTime;
            currentVelocity.y = (y - vector3.y) / deltaTime;
            currentVelocity.z = (z - vector3.z) / deltaTime;
        }

        return new Vector3(x, y, z);
    }

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
                case 2:
                    return z;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector3 index!");
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
                case 2:
                    z = value;
                    break;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector3 index!");
            }
        }
    }

    /// <summary>
    ///     <para>Creates a new vector with given x, y, z components.</para>
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    /// <summary>
    ///     <para>Creates a new vector with given x, y components and sets z to zero.</para>
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3(float x, float y)
    {
        this.x = x;
        this.y = y;
        z = 0.0f;
    }

    /// <summary>
    ///     <para>Set x, y and z components of an existing Vector3.</para>
    /// </summary>
    /// <param name="newX"></param>
    /// <param name="newY"></param>
    /// <param name="newZ"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(float newX, float newY, float newZ)
    {
        x = newX;
        y = newY;
        z = newZ;
    }

    /// <summary>
    ///     <para>Multiplies two vectors component-wise.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);

    /// <summary>
    ///     <para>Multiplies every component of this vector by the same component of scale.</para>
    /// </summary>
    /// <param name="scale"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Scale(Vector3 scale)
    {
        x *= scale.x;
        y *= scale.y;
        z *= scale.z;
    }

    /// <summary>
    ///     <para>Cross Product of two vectors.</para>
    /// </summary>
    /// <param name="lhs"></param>
    /// <param name="rhs"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 lhs, Vector3 rhs) => new((float)(lhs.y * (double)rhs.z - lhs.z * (double)rhs.y),
        (float)(lhs.z * (double)rhs.x - lhs.x * (double)rhs.z), (float)(lhs.x * (double)rhs.y - lhs.y * (double)rhs.x));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);

    /// <summary>
    ///     <para>Returns true if the given vector is exactly equal to this vector.</para>
    /// </summary>
    /// <param name="other"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object other) => other is Vector3 other1 && Equals(other1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Vector3 other) => x == (double)other.x && y == (double)other.y && z == (double)other.z;

    /// <summary>
    ///     <para>Reflects a vector off the plane defined by a normal.</para>
    /// </summary>
    /// <param name="inDirection">The direction vector towards the plane.</param>
    /// <param name="inNormal">The normal vector that defines the plane.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal)
    {
        float num = -2f * Dot(inNormal, inDirection);
        return new Vector3(num * inNormal.x + inDirection.x, num * inNormal.y + inDirection.y,
            num * inNormal.z + inDirection.z);
    }

    /// <summary>
    ///     <para>
    ///         Returns a normalized vector based on the given vector. The normalized vector has a magnitude of 1 and is in
    ///         the same direction as the given vector. Returns a zero vector If the given vector is too small to be
    ///         normalized.
    ///     </para>
    /// </summary>
    /// <param name="value">The vector to be normalized.</param>
    /// <returns>
    ///     <para>A new vector with the same direction as the original vector but with a magnitude of 1.0.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Normalize(Vector3 value)
    {
        float num = Magnitude(value);
        return num > 9.999999747378752E-06 ? value / num : zero;
    }

    /// <summary>
    ///     <para>Makes this vector have a magnitude of 1.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Normalize()
    {
        float num = Magnitude(this);
        if (num > 9.999999747378752E-06)
        {
            this = this / num;
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
    public Vector3 normalized
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Normalize(this);
    }

    /// <summary>
    ///     <para>Dot Product of two vectors.</para>
    /// </summary>
    /// <param name="lhs"></param>
    /// <param name="rhs"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3 lhs, Vector3 rhs) =>
        (float)(lhs.x * (double)rhs.x + lhs.y * (double)rhs.y + lhs.z * (double)rhs.z);

    /// <summary>
    ///     <para>Projects a vector onto another vector.</para>
    /// </summary>
    /// <param name="vector"></param>
    /// <param name="onNormal"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Project(Vector3 vector, Vector3 onNormal)
    {
        float num1 = Dot(onNormal, onNormal);
        if (num1 < (double)Mathf.Epsilon)
        {
            return zero;
        }

        float num2 = Dot(vector, onNormal);
        return new Vector3(onNormal.x * num2 / num1, onNormal.y * num2 / num1, onNormal.z * num2 / num1);
    }

    /// <summary>
    ///     <para>Projects a vector onto a plane.</para>
    /// </summary>
    /// <param name="vector">The vector to project on the plane.</param>
    /// <param name="planeNormal">The normal which defines the plane to project on.</param>
    /// <returns>
    ///     <para>The orthogonal projection of vector on the plane.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
    {
        float num1 = Dot(planeNormal, planeNormal);
        if (num1 < (double)Mathf.Epsilon)
        {
            return vector;
        }

        float num2 = Dot(vector, planeNormal);
        return new Vector3(vector.x - planeNormal.x * num2 / num1, vector.y - planeNormal.y * num2 / num1,
            vector.z - planeNormal.z * num2 / num1);
    }

    /// <summary>
    ///     <para>Calculates the angle between two vectors.</para>
    /// </summary>
    /// <param name="from">The vector from which the angular difference is measured.</param>
    /// <param name="to">The vector to which the angular difference is measured.</param>
    /// <returns>
    ///     <para>The angle in degrees between the two vectors.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Angle(Vector3 from, Vector3 to)
    {
        float num = (float)Math.Sqrt(from.sqrMagnitude * (double)to.sqrMagnitude);
        return num < 1.0000000036274937E-15
            ? 0.0f
            : (float)Math.Acos(Mathf.Clamp(Dot(from, to) / num, -1f, 1f)) * 57.29578f;
    }

    /// <summary>
    ///     <para>Calculates the signed angle between vectors from and to in relation to axis.</para>
    /// </summary>
    /// <param name="from">The vector from which the angular difference is measured.</param>
    /// <param name="to">The vector to which the angular difference is measured.</param>
    /// <param name="axis">A vector around which the other vectors are rotated.</param>
    /// <returns>
    ///     <para>Returns the signed angle between from and to in degrees.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
    {
        float num1 = Angle(from, to);
        float num2 = (float)(from.y * (double)to.z - from.z * (double)to.y);
        float num3 = (float)(from.z * (double)to.x - from.x * (double)to.z);
        float num4 = (float)(from.x * (double)to.y - from.y * (double)to.x);
        float num5 = Mathf.Sign((float)(axis.x * (double)num2 + axis.y * (double)num3 + axis.z * (double)num4));
        return num1 * num5;
    }

    /// <summary>
    ///     <para>Returns the distance between a and b.</para>
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector3 a, Vector3 b)
    {
        float num1 = a.x - b.x;
        float num2 = a.y - b.y;
        float num3 = a.z - b.z;
        return (float)Math.Sqrt(num1 * (double)num1 + num2 * (double)num2 + num3 * (double)num3);
    }

    /// <summary>
    ///     <para>Returns a copy of vector with its magnitude clamped to maxLength.</para>
    /// </summary>
    /// <param name="vector"></param>
    /// <param name="maxLength"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ClampMagnitude(Vector3 vector, float maxLength)
    {
        float sqrMagnitude = vector.sqrMagnitude;
        if (sqrMagnitude <= maxLength * (double)maxLength)
        {
            return vector;
        }

        float num1 = (float)Math.Sqrt(sqrMagnitude);
        float num2 = vector.x / num1;
        float num3 = vector.y / num1;
        float num4 = vector.z / num1;
        return new Vector3(num2 * maxLength, num3 * maxLength, num4 * maxLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Magnitude(Vector3 vector) =>
        (float)Math.Sqrt(vector.x * (double)vector.x + vector.y * (double)vector.y + vector.z * (double)vector.z);

    /// <summary>
    ///     <para>Returns the length of this vector (Read Only).</para>
    /// </summary>
    public float magnitude
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (float)Math.Sqrt(x * (double)x + y * (double)y + z * (double)z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SqrMagnitude(Vector3 vector) =>
        (float)(vector.x * (double)vector.x + vector.y * (double)vector.y + vector.z * (double)vector.z);

    /// <summary>
    ///     <para>Returns the squared length of this vector (Read Only).</para>
    /// </summary>
    public float sqrMagnitude
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (float)(x * (double)x + y * (double)y + z * (double)z);
    }

    /// <summary>
    ///     <para>Returns a vector that is made from the smallest components of two vectors.</para>
    /// </summary>
    /// <param name="lhs"></param>
    /// <param name="rhs"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Min(Vector3 lhs, Vector3 rhs) =>
        new(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z));

    /// <summary>
    ///     <para>Returns a vector that is made from the largest components of two vectors.</para>
    /// </summary>
    /// <param name="lhs"></param>
    /// <param name="rhs"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Max(Vector3 lhs, Vector3 rhs) =>
        new(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z));

    /// <summary>
    ///     <para>Shorthand for writing Vector3(0, 0, 0).</para>
    /// </summary>
    public static Vector3 zero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, 0.0f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(1, 1, 1).</para>
    /// </summary>
    public static Vector3 one
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(1f, 1f, 1f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(0, 0, 1).</para>
    /// </summary>
    public static Vector3 forward
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, 0.0f, 1f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(0, 0, -1).</para>
    /// </summary>
    public static Vector3 back
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, 0.0f, -1f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(0, 1, 0).</para>
    /// </summary>
    public static Vector3 up
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, 1f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(0, -1, 0).</para>
    /// </summary>
    public static Vector3 down
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(0.0f, -1f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(-1, 0, 0).</para>
    /// </summary>
    public static Vector3 left
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(-1f, 0.0f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(1, 0, 0).</para>
    /// </summary>
    public static Vector3 right
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(1f, 0.0f, 0.0f);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity).</para>
    /// </summary>
    public static Vector3 positiveInfinity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>
    ///     <para>Shorthand for writing Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity).</para>
    /// </summary>
    public static Vector3 negativeInfinity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    } = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator *(Vector3 a, float d) => new(a.x * d, a.y * d, a.z * d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator *(float d, Vector3 a) => new(a.x * d, a.y * d, a.z * d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 operator /(Vector3 a, float d) => new(a.x / d, a.y / d, a.z / d);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Vector3 lhs, Vector3 rhs)
    {
        float num1 = lhs.x - rhs.x;
        float num2 = lhs.y - rhs.y;
        float num3 = lhs.z - rhs.z;
        return num1 * (double)num1 + num2 * (double)num2 + num3 * (double)num3 < 9.999999439624929E-11;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Vector3 lhs, Vector3 rhs) => !(lhs == rhs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(System.Numerics.Vector3 numVec) => new(numVec.X, numVec.Y, numVec.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator System.Numerics.Vector3(Vector3 customVec) =>
        new(customVec.x, customVec.y, customVec.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(RcVec3f customVec) => new(customVec.X, customVec.Y, customVec.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RcVec3f(Vector3 customVec) => new(customVec.x, customVec.y, customVec.z);

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

        return AvalonString.Format("({0}, {1}, {2})", x.ToString(format, formatProvider),
            y.ToString(format, formatProvider), z.ToString(format, formatProvider));
    }
}
