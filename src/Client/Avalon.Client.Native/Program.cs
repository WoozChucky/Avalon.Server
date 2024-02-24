using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Timer = System.Timers.Timer;

namespace Avalon.Client.Native;

public class Program
{
    private static IWindow? _window;
    private static IInputContext? _input;
    private static GL _gl;
    private static uint _vao;
    private static uint _vbo;
    private static uint _ebo;
    private static uint _shader;
    
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello World!");
        
        WindowOptions options = WindowOptions.Default with
        {
            Title = "Avalon",
            Size = new Vector2D<int>(1920, 1080),
            VSync = false,
            VideoMode = new VideoMode(new Vector2D<int>(1920, 1080), 60),
            WindowBorder = WindowBorder.Fixed,
            WindowState = WindowState.Normal,
            API = GraphicsAPI.Default,
        };
        
        _window = Window.Create(options);
        
        _window.Load += WindowOnLoad;
        _window.Update += WindowOnUpdate;
        _window.Render += WindowOnRender;
        _window.Closing += WindowOnClosing;
        _window.Run();
        
        Console.WriteLine("Goodbye World!");
    }

    struct VertexBuffer : IDisposable
    {
        public int Size => Vertices.Length * Vertex.Size * sizeof(float);
        
        public Vertex[] Vertices;
        
        private IntPtr _buffer;

        public unsafe float* GetMemory()
        {
            if (_buffer == IntPtr.Zero)
            {
                _buffer = Marshal.AllocHGlobal(Size);
            
                var buffer = (float*) _buffer;
                for (var i = 0; i < Vertices.Length; i++)
                {
                    buffer[i * 5] = Vertices[i].Position.X;
                    buffer[i * 5 + 1] = Vertices[i].Position.Y;
                    buffer[i * 5 + 2] = Vertices[i].Position.Z;
                    buffer[i * 5 + 3] = Vertices[i].TextureCoord.X;
                    buffer[i * 5 + 4] = Vertices[i].TextureCoord.Y;
                }
                return buffer;
            }

            return (float*) _buffer;
        }

        public void Dispose()
        {
            if (_buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(_buffer);
        }
    }
    
    struct Vertex
    {
        public static int Size => PositionSize + TextureCoordSize;
        public static int PositionSize => 3;
        public static int TextureCoordSize => 2;
        public static int PositionOffset => 0;
        public static int TextureCoordOffset => 3;
        
        public Vector3 Position;
        public Vector2 TextureCoord;
    }
    
    private static unsafe void WindowOnLoad()
    {
        _input = _window!.CreateInput();
        
        _input.ConnectionChanged += InputOnConnectionChanged;
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += KOnKeyDown;
            keyboard.KeyUp += KOnKeyUp;
            keyboard.KeyChar += KOnChar;
        }
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += OnMouseButtonDown;
            mouse.MouseUp += OnMouseButtonUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Click += OnMouseClick;
            mouse.DoubleClick += OnMouseDoubleClick;
            mouse.Scroll += OnMouseScroll;
        }

        _gl = _window.CreateOpenGL();
        _gl.ClearColor(Color.CornflowerBlue);
        
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);
        
        float[] vertices =
        {
            // aPosition          // aTextureCoord
            0.5f,  0.5f, 0.0f,    1.0f, 1.0f,
            0.5f, -0.5f, 0.0f,    1.0f, 0.0f,
            -0.5f, -0.5f, 0.0f,   0.0f, 0.0f,
            -0.5f,  0.5f, 0.0f,   0.0f, 1.0f
        };
        
        var vertexBuffer = new VertexBuffer
        {
            Vertices =
            [
                new Vertex { Position = new Vector3(0.5f, 0.5f, 0.0f), TextureCoord = new Vector2(1.0f, 1.0f) },
                new Vertex { Position = new Vector3(0.5f, -0.5f, 0.0f), TextureCoord = new Vector2(1.0f, 0.0f) },
                new Vertex { Position = new Vector3(-0.5f, -0.5f, 0.0f), TextureCoord = new Vector2(0.0f, 0.0f) },
                new Vertex { Position = new Vector3(-0.5f, 0.5f, 0.0f), TextureCoord = new Vector2(0.0f, 1.0f) }
            ]
        };
        
        //fixed (float* buf = vertices)
        //    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint) (vertices.Length * sizeof(float)), buf, BufferUsageARB.StaticDraw);

        
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint) vertexBuffer.Size, vertexBuffer.GetMemory(), BufferUsageARB.StaticDraw);

        uint[] indices =
        {
            0u, 1u, 3u,
            1u, 2u, 3u
        };
        
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint) (indices.Length * sizeof(uint)), indices.AsSpan().GetPinnableReference(), BufferUsageARB.StaticDraw);
        
        const string vertexCode = @"
#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTextureCoord;

out vec2 frag_texCoords;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
    frag_texCoords = aTextureCoord;
}";
        const string fragmentCode = @"
#version 330 core

in vec2 frag_texCoords;

out vec4 out_color;

void main()
{
    out_color = vec4(frag_texCoords.x, frag_texCoords.y, 0.0, 1.0);
}";
        
        var vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexCode);
        _gl.CompileShader(vertexShader);
        _gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vStatus);
        if (vStatus != (int) GLEnum.True)
            throw new Exception("Vertex shader failed to compile: " + _gl.GetShaderInfoLog(vertexShader));
        
        var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentCode);
        _gl.CompileShader(fragmentShader);
        _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fStatus);
        if (fStatus != (int) GLEnum.True)
            throw new Exception("Fragment shader failed to compile: " + _gl.GetShaderInfoLog(fragmentShader));
        
        _shader = _gl.CreateProgram();
        _gl.AttachShader(_shader, vertexShader);
        _gl.AttachShader(_shader, fragmentShader);
        _gl.LinkProgram(_shader);
        
        _gl.GetProgram(_shader, ProgramPropertyARB.LinkStatus, out int lStatus);
        if (lStatus != (int) GLEnum.True)
            throw new Exception("Program failed to link: " + _gl.GetProgramInfoLog(_shader));
        
        _gl.DetachShader(_shader, vertexShader);
        _gl.DetachShader(_shader, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);
        
        const uint positionLoc = 0;
        _gl.EnableVertexAttribArray(positionLoc);
        _gl.VertexAttribPointer(positionLoc, Vertex.PositionSize, VertexAttribPointerType.Float, false, (uint)Vertex.Size * sizeof(float), (void *)Vertex.PositionOffset);
        
        const uint texCoordLoc = 1;
        _gl.EnableVertexAttribArray(texCoordLoc);
        _gl.VertexAttribPointer(texCoordLoc, Vertex.TextureCoordSize, VertexAttribPointerType.Float, false, (uint)Vertex.Size * sizeof(float), (void*)(Vertex.TextureCoordOffset * sizeof(float)));
        
        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
        
        var timer = new Timer(150);
        timer.Elapsed += (_, _) =>
        {
            //_window!.Title = $"Avalon | FPS: {_window.FramesPerSecond} | UPS: {_window.UpdatesPerSecond}";
        };
        timer.Start();
    }
    
    private static void WindowOnUpdate(double deltaTime)
    {
        
    }
    
    private static unsafe void WindowOnRender(double deltaTime)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        
        _gl.BindVertexArray(_vao);
        _gl.UseProgram(_shader);
        _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*) 0);
    }
    
    private static void WindowOnClosing()
    {
        Console.WriteLine("Window Disposed!");
        _input!.Dispose();
        Console.WriteLine("Input Disposed!");
    }
    
    private static void OnMouseScroll(IMouse arg1, ScrollWheel arg2)
    {
        Console.WriteLine("Mouse Scrolled!");
    }

    private static void OnMouseDoubleClick(IMouse arg1, MouseButton arg2, Vector2 arg3)
    {
        Console.WriteLine("Mouse Double Clicked!");
    }

    private static void OnMouseClick(IMouse arg1, MouseButton arg2, Vector2 arg3)
    {
        Console.WriteLine("Mouse Clicked!");
    }

    private static void OnMouseMove(IMouse arg1, Vector2 arg2)
    {
        Console.WriteLine("Mouse Moved!");
    }

    private static void OnMouseButtonUp(IMouse arg1, MouseButton arg2)
    {
        Console.WriteLine("Mouse Button Up!");
    }

    private static void OnMouseButtonDown(IMouse arg1, MouseButton arg2)
    {
        Console.WriteLine("Mouse Button Down!");
    }

    private static void KOnChar(IKeyboard keyboard, char arg2)
    {
        
    }

    private static void KOnKeyUp(IKeyboard keyboard, Key key, int keyCode)
    {
        
    }

    private static void KOnKeyDown(IKeyboard keyboard, Key key, int keyCode)
    {
        if (key == Key.Escape)
        {
            _window!.Close();
        }
    }

    private static void InputOnConnectionChanged(IInputDevice device, bool arg2)
    {
        
    }
}
