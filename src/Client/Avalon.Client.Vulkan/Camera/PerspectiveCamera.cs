// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Camera;

public class PerspectiveCamera : ICamera
{
    private float aspect = 1;
    private readonly float bottom = 40;

    private readonly float far = 100f;

    // orthographic stuff
    private float fov = 50;

    private Vector3 frontVec;

    //private float frustumPrevious;

    private readonly Vector3 globalUp = -Vector3.UnitY;
    private float height = 80f;
    private readonly float left = -40;

    private readonly float maxFov = 60f;
    private readonly float near = 0.01f;

    private float pitch;


    //private float zoomMin = 0.01f;
    //private float zoomMax = 700f;

    //private float zoomAccelerationWheel = 80f;
    //private float zoomSpeedWheel = 10f;

    //private float zoomAccelerationMouse = 40f;
    //private float zoomSpeedMouse = 80f;

    private readonly float pitchClamp = 89.99f * MathF.PI / 180f;


    private Vector3 position;
    private readonly float right = 40;


    private readonly float top = -40;
    private float width = 80f;

    private float yaw;


    public PerspectiveCamera(Vector3 position, float fovDegrees, float pitchDeg, float yawDeg,
        Vector2D<int> frameBufferSize)
    {
        fov = Scalar.DegreesToRadians(fovDegrees);
        maxFov = Scalar.DegreesToRadians(50);
        near = 0.001f;
        far = 100f;
        Pitch = pitchDeg * MathF.PI / 180f;
        Yaw = yawDeg * MathF.PI / 180f;
        //frustumPrevious = frustum;


        this.position = position;

        frontVec = Vector3.UnitZ;
        UpVec = globalUp;
        RightVec = Vector3.UnitX;

        Resize(0, 0, (uint)frameBufferSize.X, (uint)frameBufferSize.Y);
    }

    public Vector3 Target { get; private set; }

    public uint Wp { get; private set; }

    public uint Hp { get; private set; }

    public int Xp { get; private set; }

    public int Yp { get; private set; }

    public float Frustum { get; } = 0;

    public float AspectRatio => (left - right) / (top - bottom);
    public bool EnableRotation { get; set; } = true;

    public Vector3 Position
    {
        get => position;
        set => position = value;
    }

    public Vector3 FrontVec
    {
        get => frontVec;
        set => frontVec = value;
    }

    public Vector4 GetFrontVec4() => new(frontVec.X, frontVec.Y, frontVec.Z, 0f);
    public Vector3 UpVec { get; set; }

    public Vector3 RightVec { get; set; }

    public float Pitch
    {
        get => pitch;
        set
        {
            float angle = Math.Clamp(value, -pitchClamp, pitchClamp);
            pitch = angle;
            UpdateVectors();
        }
    }

    public float PitchDegrees => pitch / MathF.PI * 180f;

    public float Yaw
    {
        get => yaw;
        set
        {
            yaw = value;
            UpdateVectors();
        }
    }

    public float YawDegrees => yaw / MathF.PI * 180f;


    //private Matrix4x4 inverseView = Matrix4x4.Identity;
    public Matrix4x4 GetInverseViewMatrix()
    {
        _ = Matrix4x4.Invert(GetViewMatrix(), out Matrix4x4 ret);
        return ret;
    }


    // Get the view matrix using the amazing LookAt function described more in depth on the web tutorials
    public Matrix4x4 GetViewMatrix() =>
        //var mat = Matrix4x4.CreateLookAt(position, position + frontVec, upVec);
        //_ = Matrix4x4.Invert(mat, out inverseView);
        Matrix4x4.CreateLookAt(position, Target, UpVec);

    // Get the projection matrix using the same method we have used up until this point
    public Matrix4x4 GetProjectionMatrix() => Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, near, far);

    public Vector2 Project(Vector3 mouse3d)
    {
        Vector4 vec;

        vec.X = mouse3d.X;
        vec.Y = mouse3d.Y;
        vec.Z = mouse3d.Z;
        vec.W = 1.0f;

        vec = Vector4.Transform(vec, GetViewMatrix());
        vec = Vector4.Transform(vec, GetProjectionMatrix());

        if (vec.W > float.Epsilon || vec.W < -float.Epsilon)
        {
            vec.X /= vec.W;
            vec.Y /= vec.W;
            vec.Z /= vec.W;
        }

        return new Vector2(vec.X, vec.Y);
    }

    public Vector3 UnProject(Vector2 mouse2d)
    {
        Vector4 vec;

        vec.X = mouse2d.X;
        vec.Y = mouse2d.Y;
        vec.Z = 0f;
        vec.W = 1.0f;

        Matrix4x4.Invert(GetViewMatrix(), out Matrix4x4 viewInv);
        Matrix4x4.Invert(GetProjectionMatrix(), out Matrix4x4 projInv);

        vec = Vector4.Transform(vec, projInv);
        vec = Vector4.Transform(vec, viewInv);

        if (vec.W > float.Epsilon || vec.W < -float.Epsilon)
        {
            vec.X /= vec.W;
            vec.Y /= vec.W;
            vec.Z /= vec.W;
        }

        return new Vector3(vec.X, vec.Y, vec.Z);
    }


    public void Pan(Vector3 vStart, Vector3 vStop)
    {
        Vector3 vdiff = vStart - vStop;
        Console.WriteLine(
            $"position=[{position.X:+0.0000;-0.0000},{position.Y:+0.0000;-0.0000},{position.Z:+0.0000;-0.0000}");

        position += vdiff * 2f;
        Target += vdiff * 2f;
        //UpdateVectors();
    }

    public void Rotate(Vector2 vStart, Vector2 vStop)
    {
        if (!EnableRotation)
        {
            return;
        }

        Console.WriteLine(
            $"[{vStart.X:+0.0000;-0.0000},{vStart.Y:+0.0000;-0.0000}] to [{vStop.X:+0.0000;-0.0000},{vStop.Y:+0.0000;-0.0000}]");
        float sx = 1.1f;
        Yaw += (vStop.X - vStart.X) * sx * aspect;
        Pitch -= (vStop.Y - vStart.Y) * sx;
        //Console.WriteLine($"pitch={pitch * 180f / MathF.PI:0.0000}, yaw={yaw * 180f / MathF.PI:0.0000}");
        UpdateVectors();
    }

    public void ZoomIncremental(float amount)
    {
        fov += amount * .01f;
        if (fov < 1.0f)
        {
            fov = 1.0f;
        }

        if (fov > maxFov)
        {
            fov = maxFov;
        }
        //frustum = frustumPrevious + amount * zoomSpeedWheel * frustumPrevious / zoomAccelerationWheel;
        //frustum = Math.Clamp(frustum, zoomMin, zoomMax);
        //frustumPrevious = frustum;
        //updateOrtho();
    }

    //public void ZoomMouse(float amount)
    //{
    //    frustum = frustumPrevious + amount * zoomSpeedMouse * frustumPrevious / zoomAccelerationMouse;
    //    frustum = Math.Clamp(frustum, zoomMin, zoomMax);
    //    left = (frustum * aspect) / -2f;
    //    right = (frustum * aspect) / 2f;
    //    width = right - left;
    //    top = frustum / 2f;
    //    bottom = frustum / -2f;
    //    height = bottom - top;
    //}

    public void ZoomSetPrevious()
    {
        //frustumPrevious = frustum;
    }
    //public void FitHeight(float fitHeight)
    //{
    //    frustum = fitHeight;
    //    frustumPrevious = frustum;
    //    updateOrtho();
    //}


    public void Resize(int xp, int yp, uint wp, uint hp)
    {
        this.Xp = xp;
        this.Yp = yp;
        this.Wp = wp;
        this.Hp = hp;

        aspect = wp / (float)hp;
        width = wp;
        height = hp;

        //updateOrtho();

        UpdateVectors();
        //Console.WriteLine($" camera {Name} | Resized {wp}x{hp}");
    }

    public void Reset()
    {
    }


    private void updatePerspective()
    {
        // Vulkan does top = negative
        //left = (frustum * aspect) / -2f;
        //right = (frustum * aspect) / 2f;
        //width = right - left;
        //top = frustum / -2f;
        //bottom = frustum / 2f;
        //height = bottom - top;
    }


    // This function is going to update the direction vertices using some of the math learned in the web tutorials
    private void UpdateVectors()
    {
        // First the front matrix is calculated using some basic trigonometry
        frontVec.X = (float)Math.Cos(pitch) * (float)Math.Cos(yaw);
        frontVec.Y = (float)Math.Sin(pitch);
        frontVec.Z = (float)Math.Cos(pitch) * (float)Math.Sin(yaw);

        // We need to make sure the vectors are all normalized, as otherwise we would get some funky results
        frontVec = Vector3.Normalize(frontVec);

        // Calculate both the right and the up vector using cross product
        // Note that we are calculating the right from the global up, this behaviour might
        // not be what you need for all cameras so keep this in mind if you do not want a FPS camera
        RightVec = Vector3.Normalize(Vector3.Cross(frontVec, globalUp));
        UpVec = Vector3.Normalize(Vector3.Cross(RightVec, frontVec));
    }
}
