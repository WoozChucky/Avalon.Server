using Avalon.Common.Mathematics;
using Xunit;

namespace Avalon.Shared.UnitTests.Mathematics;

public class Vector3Should
{
    [Fact]
    public void Compare_by_components()
    {
        var v1 = new Vector3(1f, 2f, 3f);
        var v2 = new Vector3(1f, 2f, 3f);
        var other = new Vector3(4f, 5f, 6f);

        Assert.Equal(v1, v2);
        Assert.True(v1 == v2);
        Assert.False(v1 != v2);
        Assert.NotEqual(v1, other);
        Assert.False(v1 == other);
        Assert.True(v1 != other);
    }

    [Fact]
    public void Do_the_arithmetic_operators()
    {
        Assert.Equal(new Vector3(5f, 7f, 9f), new Vector3(1f, 2f, 3f) + new Vector3(4f, 5f, 6f));
        Assert.Equal(new Vector3(4f, 5f, 6f), new Vector3(5f, 7f, 9f) - new Vector3(1f, 2f, 3f));
        Assert.Equal(new Vector3(2f, 4f, 6f), new Vector3(1f, 2f, 3f) * 2f);
        Assert.Equal(new Vector3(2f, 4f, 6f), 2f * new Vector3(1f, 2f, 3f));
        Assert.Equal(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f) / 2f);
    }

    [Fact]
    public void Measure_length_distance_dot_and_angle()
    {
        var v = new Vector3(3f, 0f, 4f);

        Assert.Equal(5f, v.magnitude);
        Assert.Equal(25f, v.sqrMagnitude);
        Assert.Equal(5f, Vector3.Distance(Vector3.zero, v));
        // 1*4 + 2*5 + 3*6 = 4 + 10 + 18 = 32
        Assert.Equal(32f, Vector3.Dot(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f)));
        Assert.Equal(90f, Vector3.Angle(new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)), precision: 3);
        Assert.Equal(0f, Vector3.Angle(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f)), precision: 3);
    }

    [Fact]
    public void Normalize_and_leave_a_zero_vector_at_zero()
    {
        Vector3 normalized = new Vector3(5f, 0f, 0f).normalized;
        Assert.Equal(new Vector3(1f, 0f, 0f), normalized);
        Assert.Equal(1f, normalized.magnitude);
        Assert.Equal(new Vector3(0f, 1f, 0f), Vector3.Normalize(new Vector3(0f, 5f, 0f)));
        Assert.Equal(Vector3.zero, Vector3.Normalize(Vector3.zero));
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

    [Fact]
    public void Interpolate_and_move_towards_a_target()
    {
        var from = new Vector3(0f, 0f, 0f);
        var to = new Vector3(10f, 10f, 10f);

        Assert.Equal(new Vector3(5f, 5f, 5f), Vector3.Lerp(from, to, 0.5f));
        Assert.Equal(from, Vector3.Lerp(from, to, -1f));
        Assert.Equal(to, Vector3.Lerp(from, to, 2f));
        Assert.Equal(new Vector3(20f, 20f, 20f), Vector3.LerpUnclamped(from, to, 2f));

        var target = new Vector3(3f, 4f, 0f); // distance=5
        Assert.Equal(target, Vector3.MoveTowards(from, target, 10f));
        Assert.Equal(3f, Vector3.MoveTowards(from, new Vector3(10f, 0f, 0f), 3f).x, precision: 5);
    }

    [Fact]
    public void Cross_scale_and_reflect()
    {
        var right = new Vector3(1f, 0f, 0f);
        var up = new Vector3(0f, 1f, 0f);
        var forward = Vector3.Cross(right, up);
        Assert.Equal(new Vector3(0f, 0f, 1f), forward);
        Assert.Equal(0f, Vector3.Dot(forward, right), precision: 5);
        Assert.Equal(0f, Vector3.Dot(forward, up), precision: 5);

        Assert.Equal(new Vector3(10f, 18f, 28f), Vector3.Scale(new Vector3(2f, 3f, 4f), new Vector3(5f, 6f, 7f)));

        // Reflecting (1,-1,0) over up normal (0,1,0) should give (1,1,0)
        var reflected = Vector3.Reflect(new Vector3(1f, -1f, 0f), up);
        Assert.Equal(1f, reflected.x, precision: 5);
        Assert.Equal(1f, reflected.y, precision: 5);
        Assert.Equal(0f, reflected.z, precision: 5);
    }

    [Fact]
    public void Project_onto_a_vector_and_onto_a_plane()
    {
        // Projecting (3,4,0) onto x-axis should give (3,0,0)
        var onAxis = Vector3.Project(new Vector3(3f, 4f, 0f), new Vector3(1f, 0f, 0f));
        Assert.Equal(3f, onAxis.x, precision: 5);
        Assert.Equal(0f, onAxis.y, precision: 5);
        Assert.Equal(0f, onAxis.z, precision: 5);

        Assert.Equal(Vector3.zero, Vector3.Project(new Vector3(1f, 2f, 3f), Vector3.zero));

        // Vector (1,1,0) projected onto XZ plane (normal = up) should give (1,0,0)
        var onPlane = Vector3.ProjectOnPlane(new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f));
        Assert.Equal(1f, onPlane.x, precision: 5);
        Assert.Equal(0f, onPlane.y, precision: 5);
        Assert.Equal(0f, onPlane.z, precision: 5);
    }

    [Fact]
    public void Clamp_the_magnitude_down_but_never_up()
    {
        Assert.Equal(2f, Vector3.ClampMagnitude(new Vector3(3f, 4f, 0f), 2f).magnitude, precision: 5);
        Assert.Equal(new Vector3(1f, 0f, 0f), Vector3.ClampMagnitude(new Vector3(1f, 0f, 0f), 5f));
    }

    [Fact]
    public void Read_and_write_its_components()
    {
        var v = new Vector3(1f, 2f, 3f);
        Assert.Equal(1f, v[0]);
        Assert.Equal(2f, v[1]);
        Assert.Equal(3f, v[2]);
        Assert.Throws<IndexOutOfRangeException>(() => v[3]);

        v.Set(7f, 8f, 9f);
        Assert.Equal((7f, 8f, 9f), (v.x, v.y, v.z));

        Assert.Equal(0f, new Vector3(5f, 6f).z);
    }
}
