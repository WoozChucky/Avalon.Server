// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Camera;

namespace Avalon.Client.Vulkan.Controls;

public class CameraController
{
    private readonly ICamera camera = null!;


    private MouseButtonState btnLeft = MouseButtonState.None;
    private MouseButtonState btnMid = MouseButtonState.None;
    private MouseButtonState btnRight = MouseButtonState.None;

    private MouseControlState controlState = MouseControlState.None;
    private MouseControlState controlStatePrevious = MouseControlState.None;

    private Vector2 mouse2d = Vector2.Zero;
    private Vector2 mouse2dStartDrag = Vector2.Zero;
    private Vector3 mouse3d = Vector3.Zero;

    private Vector3 mouse3dStartDrag = Vector3.Zero;

    //private long mouse3dStartTicks;
    private float mouseWheel;
    public Vector2D<int> screen;

    public CameraController(ICamera camera, IWindow window, IInputContext input)
    {
        this.camera = camera;

        input.ConnectionChanged += DoConnect;

        Resize(window.FramebufferSize);

        foreach (IMouse mouse in input.Mice)
        {
            if (!mouse.IsConnected)
            {
                continue;
            }

            DoConnect(mouse, mouse.IsConnected);
        }
    }

    // debugging data for output to screen
    private string getMouseString()
    {
        string ml = btnLeft == MouseButtonState.Down ? "LX" : "L_";
        string mm = btnMid == MouseButtonState.Down ? "MX" : "M_";
        string mr = btnRight == MouseButtonState.Down ? "RX" : "R_";
        return $"{controlState,-16} [{ml} {mm} {mr}] | " +
               $"2D=[{mouse2d.X:+0.0000;-0.0000},{mouse2d.Y:+0.0000;-0.0000}] | " +
               $"3D=[{mouse3d.X:+0.0000;-0.0000},{mouse3d.Y:+0.0000;-0.0000},{mouse3d.Z:+0.0000;-0.0000}] | " +
               $"Pos=[{camera.Position.X:+0.0000;-0.0000},{camera.Position.Y:+0.0000;-0.0000},{camera.Position.Z:+0.0000;-0.0000}] | " +
               $"Frustum={camera.Frustum:0.00}, Pitch={camera.PitchDegrees:0.0}°, Yaw={camera.YawDegrees:0.0}";
    }

    public MouseState GetMouseState() =>
        new()
        {
            Pos2d = mouse2d,
            StartDrag2d = mouse2dStartDrag,
            Pos3d = mouse3d,
            StartDrag3d = mouse3dStartDrag,
            Wheel = mouseWheel,
            ControlState = controlState,
            Debug = getMouseString()
        };


    //public Action<MouseState> OnMouseStateChanged = null!;


    // send changes to camera
    private void updateCamera()
    {
        switch (controlState)
        {
            case MouseControlState.Pick:
                break;
            case MouseControlState.Context:
                // Debugging view/projection matrices
                //Console.WriteLine("");
                //Console.WriteLine("position");
                //Console.WriteLine($"[{camera.Position.X,-10:0.000},{camera.Position.Y,-10:0.000},{camera.Position.Z,-10:0.000}]");
                //Console.WriteLine("view matrix");
                //Console.WriteLine(camera.GetViewMatrix().PrintCols());
                //Console.WriteLine("inverse view matrix");
                //Console.WriteLine(camera.GetInverseViewMatrix().PrintCols());
                //Console.WriteLine(camera.GetViewMatrixGlm().PrintCols());
                //Console.WriteLine("projection matrix");
                //Console.WriteLine(camera.GetProjectionMatrix().PrintCols());
                //Console.WriteLine(camera.GetProjectionMatrixGlm().PrintCols());

                break;
            case MouseControlState.Pan:
                camera.Pan(mouse3dStartDrag, mouse3d);
                break;
            case MouseControlState.ZoomWheel:
                camera.ZoomIncremental(mouseWheel);
                break;
            case MouseControlState.Rotate:
                camera.Rotate(mouse2dStartDrag, mouse2d);
                mouse2dStartDrag = mouse2d;
                break;
        }
    }

    // start drag events and set state
    private void mouseButton()
    {
        controlState = (l: btnLeft, m: btnMid, r: btnRight) switch
        {
            var s when
                s.l != MouseButtonState.Down &&
                s.m == MouseButtonState.Down &&
                s.r == MouseButtonState.Down => MouseControlState.Rotate,
            var s when
                s.l != MouseButtonState.Down &&
                s.m == MouseButtonState.Down &&
                s.r != MouseButtonState.Down &&
                controlStatePrevious != MouseControlState.Rotate => MouseControlState.Pan,
            var s when
                s.l == MouseButtonState.Down &&
                s.m != MouseButtonState.Down &&
                s.r != MouseButtonState.Down => MouseControlState.Pick,
            var s when
                s.l != MouseButtonState.Down &&
                s.m != MouseButtonState.Down &&
                s.r == MouseButtonState.Down => MouseControlState.Context,
            var s when
                s.l != MouseButtonState.Down &&
                s.m != MouseButtonState.Down &&
                s.r != MouseButtonState.Down &&
                MathF.Abs(mouseWheel) > .01 => MouseControlState.ZoomWheel,
            _ => MouseControlState.None
        };

        Console.WriteLine(controlState.ToString());

        switch (controlState)
        {
            case MouseControlState.Pan:
                mouse3dStartDrag = mouse3d;
                break;
            case MouseControlState.Rotate:
                mouse2dStartDrag = mouse2d;
                break;
            case MouseControlState.Pick:
                break;
            case MouseControlState.Context:
                break;
        }

        controlStatePrevious = controlState;
    }

    public void Resize(Vector2D<int> frameBufferSize) => screen = frameBufferSize;
    //camera.Resize(0, 0, (uint)screen.X, (uint)screen.Y);

    public void DoConnect(IInputDevice device, bool isConnected)
    {
        if (device is IMouse mouse)
        {
            //Console.WriteLine($"Discovered mouse {mouse.Index} (Connected: {isConnected})");
            if (isConnected)
            {
                mouse.MouseUp += MouseOnMouseUp;
                mouse.MouseDown += MouseOnMouseDown;
                mouse.Click += MouseOnClick;
                mouse.DoubleClick += MouseOnDoubleClick;
                mouse.Scroll += MouseOnScroll;
                mouse.MouseMove += MouseOnMouseMove;
            }
            else
            {
                mouse.MouseUp -= MouseOnMouseUp;
                mouse.MouseDown -= MouseOnMouseDown;
                mouse.Click -= MouseOnClick;
                mouse.DoubleClick -= MouseOnDoubleClick;
                mouse.Scroll -= MouseOnScroll;
                mouse.MouseMove -= MouseOnMouseMove;
            }
        }
    }


    private void MouseOnMouseMove(IMouse arg1, Vector2 arg2)
    {
        //Console.WriteLine($"M{arg1.Index}> Moved: {arg2}");
        float x = 2.0f * arg2.X / screen.X - 1f;
        float y = 2.0f * arg2.Y / screen.Y - 1f;

        mouse2d = new Vector2(x, y);
        mouse3d = camera.UnProject(mouse2d);
        mouseWheel = 0;
        updateCamera();
    }

    private void MouseOnScroll(IMouse arg1, ScrollWheel arg2)
    {
        mouseWheel = -arg2.Y;
        mouseButton();
        updateCamera();
    }

    private void MouseOnMouseDown(IMouse arg1, MouseButton arg2)
    {
        //Console.WriteLine($"M{arg1.Index}> {arg2} down.");
        switch (arg2)
        {
            case MouseButton.Left:
                btnLeft = MouseButtonState.Down;
                break;
            case MouseButton.Middle:
                btnMid = MouseButtonState.Down;
                break;
            case MouseButton.Right:
                btnRight = MouseButtonState.Down;
                break;
        }

        mouseButton();
        updateCamera();
    }

    private void MouseOnMouseUp(IMouse arg1, MouseButton arg2)
    {
        //Console.WriteLine($"M{arg1.Index}> {arg2} up.");
        switch (arg2)
        {
            case MouseButton.Left:
                btnLeft = MouseButtonState.Up;
                break;
            case MouseButton.Middle:
                btnMid = MouseButtonState.Up;
                break;
            case MouseButton.Right:
                btnRight = MouseButtonState.Up;
                break;
        }

        mouseButton();
        updateCamera();
    }

    private void MouseOnClick(IMouse arg1, MouseButton arg2, Vector2 pos)
    {
        //Console.WriteLine($"M{arg1.Index}> {arg2} single click.");
    }

    private void MouseOnDoubleClick(IMouse arg1, MouseButton arg2, Vector2 pos) =>
        Console.WriteLine($"M{arg1.Index}> {arg2} double click.");
}

public struct MouseState
{
    public Vector2 Pos2d;
    public Vector2 StartDrag2d;
    public Vector3 Pos3d;
    public Vector3 StartDrag3d;
    public float Wheel;
    public MouseControlState ControlState;
    public string Debug;
}

public enum MouseButtonState
{
    None,
    Up,
    Down,
    Down2
}

public enum MouseControlState
{
    None,
    Pan,

    //ZoomMouse,
    ZoomWheel,
    Rotate,
    Context,
    Pick
}
