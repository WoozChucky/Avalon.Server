// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;
using Avalon.Client.Engine;
using Avalon.Common.Mathematics;
using Vector2 = Avalon.Common.Mathematics.Vector2;
using Vector3 = Avalon.Common.Mathematics.Vector3;

namespace Avalon.Client;

public sealed class CameraThirdPerson
{
    private readonly Dictionary<CameraControls, KeyboardKey> ControlsKeys;

    // how far from the target position to the camera's view point (the zoom)
    private float CameraPullbackDistance;

    // the field of view in X and Y
    private Vector2 FOV;

    // how far up can the camera look
    private float MaximumViewY;

    // how far down can the camera look
    private float MinimumViewY;

    // how many pixels equate out to an angle move, larger numbers mean slower, more accurate mouse
    private float MouseSensitivity;

    // the speed in units/second to move
    // X = sidestep
    // Y = jump/fall
    // Z = forward
    private Vector3 MoveSpeed;

    // state for mouse movement
    private Vector2 PreviousMousePosition;

    // the speed for turning when using keys to look
    // degrees/second
    private Vector2 TurnSpeed;

    // use the mouse for looking?
    private bool UseMouse;
    private int UseMouseButton;

    // state for view angles
    public Vector2 ViewAngles;

    // the Raylib camera to pass to raylib view functions.
    public Camera3D ViewCamera;

    // the vector in the ground plane that the camera is facing
    private Vector3 ViewForward;

    public CameraThirdPerson() =>
        ControlsKeys = new Dictionary<CameraControls, KeyboardKey>
        {
            [CameraControls.MoveBack] = KeyboardKey.W,
            [CameraControls.MoveFront] = KeyboardKey.S,
            [CameraControls.MoveLeft] = KeyboardKey.A,
            [CameraControls.MoveRight] = KeyboardKey.D,
            [CameraControls.MoveUp] = KeyboardKey.Space,
            [CameraControls.MoveDown] = KeyboardKey.LeftControl,
            [CameraControls.TurnLeft] = KeyboardKey.Left,
            [CameraControls.TurnRight] = KeyboardKey.Right,
            [CameraControls.TurnUp] = KeyboardKey.Up,
            [CameraControls.TurnDown] = KeyboardKey.Down,
            [CameraControls.Sprint] = KeyboardKey.LeftShift
        };

    // the position of the base of the camera (on the floor)
    // note that this will not be the view position because it is offset by the eye height.
    // this value is also not changed by the view bobble
    public Vector3 CameraPosition { get; set; }

    // State for window focus
    public bool Focused { get; set; }

    public float NearPlane { get; set; } = 0.01f;
    public float FarPlane { get; set; } = 1000.0f;

    // called to initialize a camera to default values
    public void Setup(float fovY, Vector3 position, Vector3 speed = default)
    {
        MoveSpeed = speed;
        TurnSpeed = new Vector2(90, 90);

        MouseSensitivity = 600;

        MinimumViewY = -89.995f;
        MaximumViewY = 89.995f;

        PreviousMousePosition = GetMousePosition();
        Focused = IsWindowFocused();

        CameraPullbackDistance = 5;

        ViewAngles = new Vector2(0, 0);

        CameraPosition = position;
        FOV.y = fovY;

        ViewCamera.Target = position;
        ViewCamera.Position = ViewCamera.Target + new System.Numerics.Vector3(0, 0, CameraPullbackDistance);
        ViewCamera.Up = new System.Numerics.Vector3(0, 1, 0);
        ViewCamera.FovY = fovY;
        ViewCamera.Projection = CameraProjection.Perspective;

        NearPlane = 0.01f;
        FarPlane = 500.0f;

        ResizeTpOrbitCameraView(this);
        SetUseMouse(true, 1);
    }

    public void Update()
    {
        if (IsWindowResized())
        {
            ResizeTpOrbitCameraView(this);
        }

        bool showCursor = !UseMouse || UseMouseButton >= 0;

        if (IsWindowFocused() != Focused && !showCursor)
        {
            Focused = IsWindowFocused();
            if (Focused)
            {
                DisableCursor();
                PreviousMousePosition = GetMousePosition(); // so there is no jump on focus
            }
            else
            {
                EnableCursor();
            }
        }

        // Mouse movement detection
        Vector2 mousePositionDelta = GetMouseDelta();
        float mouseWheelMove = GetMouseWheelMove();

        // Keys input detection
        Dictionary<CameraControls, float> directions = new()
        {
            [CameraControls.MoveFront] = GetSpeedForAxis(CameraControls.MoveFront, MoveSpeed.z),
            [CameraControls.MoveBack] = GetSpeedForAxis(CameraControls.MoveBack, MoveSpeed.z),
            [CameraControls.MoveRight] = GetSpeedForAxis(CameraControls.MoveRight, MoveSpeed.x),
            [CameraControls.MoveLeft] = GetSpeedForAxis(CameraControls.MoveLeft, MoveSpeed.x),
            [CameraControls.MoveUp] = GetSpeedForAxis(CameraControls.MoveUp, MoveSpeed.y),
            [CameraControls.MoveDown] = GetSpeedForAxis(CameraControls.MoveDown, MoveSpeed.y)
        };

        bool useMouse = UseMouse && (UseMouseButton < 0 || IsMouseButtonDown((MouseButton)UseMouseButton));

        float turnRotation = GetSpeedForAxis(CameraControls.TurnRight, TurnSpeed.x) -
                             GetSpeedForAxis(CameraControls.TurnLeft, TurnSpeed.x);
        float tiltRotation = GetSpeedForAxis(CameraControls.TurnUp, TurnSpeed.y) -
                             GetSpeedForAxis(CameraControls.TurnDown, TurnSpeed.y);

        if (turnRotation != 0)
        {
            ViewAngles.x -= turnRotation * DEG2RAD;
        }
        else if (useMouse && Focused)
        {
            ViewAngles.x -= mousePositionDelta.x / MouseSensitivity;
        }

        if (tiltRotation != 0)
        {
            ViewAngles.y += tiltRotation * DEG2RAD;
        }
        else if (useMouse && Focused)
        {
            ViewAngles.y += mousePositionDelta.y / -MouseSensitivity;
        }

        // Angle clamp
        if (ViewAngles.y < MinimumViewY * DEG2RAD)
        {
            ViewAngles.y = MinimumViewY * DEG2RAD;
        }
        else if (ViewAngles.y > MaximumViewY * DEG2RAD)
        {
            ViewAngles.y = MaximumViewY * DEG2RAD;
        }

        //movement in plane rotation space
        Vector3 moveVec = new()
        {
            z = directions[CameraControls.MoveFront] - directions[CameraControls.MoveBack],
            x = directions[CameraControls.MoveRight] - directions[CameraControls.MoveLeft],
            y = directions[CameraControls.MoveUp] - directions[CameraControls.MoveDown]
        };

        // update zoom
        CameraPullbackDistance -= GetMouseWheelMove();
        if (CameraPullbackDistance < 1)
        {
            CameraPullbackDistance = 1;
        }

        // vector we are going to transform to get the camera offset from the target point
        Vector3 camPos = new() {x = 0, y = 0, z = CameraPullbackDistance};

        Matrix4x4 tiltMat = Matrix4x4.CreateRotationX(ViewAngles.y); // a matrix for the tilt rotation
        Matrix4x4 rotMat = Matrix4x4.CreateRotationY(ViewAngles.x); // a matrix for the plane rotation
        Matrix4x4 mat = tiltMat * rotMat; // the combined transformation matrix for the camera position

        camPos = Vector3.Transform(camPos, mat); // transform the camera position into a vector in world space
        moveVec = Vector3.Transform(moveVec,
            rotMat); // transform the movement vector into world space, but ignore the tilt so it is in plane

        CameraPosition += moveVec; // move the target to the moved position

        // validate cam pos here

        // set the view camera
        ViewCamera.Target = CameraPosition;
        ViewCamera.Position =
            CameraPosition + camPos; // offset the camera position by the vector from the target position
    }

    public Frustum CalculateFrustum(int screenWidth, int screenHeight)
    {
        // Get the combined view-projection matrix
        Matrix4x4 viewMatrix = GetViewMatrix();
        Matrix4x4 projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4, // Field of view
            (float)screenWidth / screenHeight, // Aspect ratio
            NearPlane, // Near plane
            FarPlane // Far plane
        );

        Matrix4x4 vpMatrix = viewMatrix * projectionMatrix;

        Frustum frustum = new();

        // Extract frustum planes from the VP matrix
        frustum.Planes[0] = new Vector4(vpMatrix.M14 + vpMatrix.M11, vpMatrix.M24 + vpMatrix.M21,
            vpMatrix.M34 + vpMatrix.M31, vpMatrix.M44 + vpMatrix.M41); // Left
        frustum.Planes[1] = new Vector4(vpMatrix.M14 - vpMatrix.M11, vpMatrix.M24 - vpMatrix.M21,
            vpMatrix.M34 - vpMatrix.M31, vpMatrix.M44 - vpMatrix.M41); // Right
        frustum.Planes[2] = new Vector4(vpMatrix.M14 - vpMatrix.M12, vpMatrix.M24 - vpMatrix.M22,
            vpMatrix.M34 - vpMatrix.M32, vpMatrix.M44 - vpMatrix.M42); // Top
        frustum.Planes[3] = new Vector4(vpMatrix.M14 + vpMatrix.M12, vpMatrix.M24 + vpMatrix.M22,
            vpMatrix.M34 + vpMatrix.M32, vpMatrix.M44 + vpMatrix.M42); // Bottom
        frustum.Planes[4] = new Vector4(vpMatrix.M13, vpMatrix.M23, vpMatrix.M33, vpMatrix.M43); // Near
        frustum.Planes[5] = new Vector4(vpMatrix.M14 - vpMatrix.M13, vpMatrix.M24 - vpMatrix.M23,
            vpMatrix.M34 - vpMatrix.M33, vpMatrix.M44 - vpMatrix.M43); // Far

        // Normalize each plane
        for (int i = 0; i < 6; i++)
        {
            float length = MathF.Sqrt(frustum.Planes[i].X * frustum.Planes[i].X +
                                      frustum.Planes[i].Y * frustum.Planes[i].Y +
                                      frustum.Planes[i].Z * frustum.Planes[i].Z);
            frustum.Planes[i] /= length;
        }

        return frustum;
    }

    // turn the use of mouselook on/off, also updates the cursor visibility and what button to use, set button to -1 to disable mouse
    private void SetUseMouse(bool useMouse, int button)
    {
        UseMouse = useMouse;
        UseMouseButton = button;

        bool showCursor = !useMouse || button >= 0;

        if (!showCursor && IsWindowFocused())
        {
            DisableCursor();
        }
        else if (showCursor && IsWindowFocused())
        {
            // EnableCursor();
        }
    }

    private float GetSpeedForAxis(CameraControls axis, float speed)
    {
        if (!ControlsKeys.TryGetValue(axis, out KeyboardKey key))
        {
            return 0;
        }

        if (key == KeyboardKey.Null)
        {
            return 0;
        }

        float factor = 1.0f;
        if (IsKeyDown(ControlsKeys[CameraControls.Sprint]))
        {
            factor = 2;
        }

        if (IsKeyDown(key))
        {
            return speed * GetFrameTime() * factor;
        }

        return 0.0f;
    }

    // update the camera for the current frame


    private float GetFOVX() => FOV.x;

    private Vector3 GetCameraPosition() => CameraPosition;

    private void SetCameraPosition(Vector3 position)
    {
        CameraPosition = position;
        Vector3 forward = ViewCamera.Target - ViewCamera.Position;
        ViewCamera.Position = CameraPosition;
        ViewCamera.Target = CameraPosition + forward;
    }

    private Ray GetViewRay() => new(ViewCamera.Position, GetForwardVector());

    private Vector3 GetForwardVector() => Vector3.Normalize(ViewCamera.Target - ViewCamera.Position);

    private Vector3 GetFowardGroundVector() => ViewForward;

    // public Matrix4x4 GetViewMatrix() => Raymath.MatrixLookAt(ViewCamera.Position, ViewCamera.Target, ViewCamera.Up);

    public Matrix4x4 GetViewMatrix()
    {
        // Calculate forward, right, and up vectors
        Vector3 forward = Vector3.Normalize(ViewCamera.Target - ViewCamera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(ViewCamera.Up, forward));
        Vector3 adjustedUp = Vector3.Cross(forward, right);

        // Create the look-at matrix
        return new Matrix4x4(
            right.x, adjustedUp.x, -forward.x, 0,
            right.y, adjustedUp.y, -forward.y, 0,
            right.z, adjustedUp.z, -forward.z, 0,
            -Vector3.Dot(right, ViewCamera.Position), -Vector3.Dot(adjustedUp, ViewCamera.Position),
            Vector3.Dot(forward, ViewCamera.Position), 1
        );
    }

    // start drawing using the camera, with near/far plane support
    public void BeginMode3D()
    {
        float aspect = (float)GetScreenWidth() / GetScreenHeight();
        SetupCamera(this, aspect);
    }

    private static void SetupCamera(CameraThirdPerson camera, float aspect)
    {
        Rlgl.DrawRenderBatchActive(); // Draw Buffers (Only OpenGL 3+ and ES2)
        Rlgl.MatrixMode(MatrixMode.Projection); // Switch to projection matrix
        Rlgl.PushMatrix(); // Save previous matrix, which contains the settings for the 2d ortho projection
        Rlgl.LoadIdentity(); // Reset current matrix (projection)

        if (camera.ViewCamera.Projection == CameraProjection.Perspective)
        {
            // Setup perspective projection
            double top = Rlgl.CULL_DISTANCE_NEAR * Mathf.Tan((float)(camera.ViewCamera.FovY * 0.5 * DEG2RAD));
            double right = top * aspect;

            Rlgl.Frustum(-right, right, -top, top, camera.NearPlane, camera.FarPlane);
        }
        else if (camera.ViewCamera.Projection == CameraProjection.Orthographic)
        {
            // Setup orthographic projection
            double top = camera.ViewCamera.FovY / 2.0;
            double right = top * aspect;

            Rlgl.Ortho(-right, right, -top, top, camera.NearPlane, camera.FarPlane);
        }

        // NOTE: zNear and zFar values are important when computing depth buffer values

        Rlgl.MatrixMode(MatrixMode.ModelView); // Switch back to modelview matrix
        Rlgl.LoadIdentity(); // Reset current matrix (modelview)

        // Setup Camera view
        Matrix4x4 matView =
            Raymath.MatrixLookAt(camera.ViewCamera.Position, camera.ViewCamera.Target, camera.ViewCamera.Up);
        Rlgl.MultMatrixf(matView); // Multiply modelview matrix by view matrix (camera)

        Rlgl.EnableDepthTest(); // Enable DEPTH_TEST for 3D
    }

    // end drawing with the camera
    public void EndMode3D() => Raylib_cs.Raylib.EndMode3D();

    private static void ResizeTpOrbitCameraView(CameraThirdPerson? camera)
    {
        if (camera == null)
        {
            return;
        }

        float width = GetScreenWidth();
        float height = GetScreenHeight();

        camera.FOV.y = camera.ViewCamera.FovY;

        if (height != 0)
        {
            camera.FOV.x = camera.FOV.y * (width / height);
        }
    }

    private enum CameraControls
    {
        MoveFront,
        MoveBack,
        MoveRight,
        MoveLeft,
        MoveUp,
        MoveDown,
        TurnLeft,
        TurnRight,
        TurnUp,
        TurnDown,
        Sprint
    }
}
