using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalon.Client.Native.Graphics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using Color = System.Drawing.Color;
using Texture = Avalon.Client.Native.Graphics.Texture;
using Timer = System.Timers.Timer;
using Vertex = Avalon.Client.Native.Graphics.Vertex;
using Window = Silk.NET.Windowing.Window;

namespace Avalon.Client.Native;

public class Program
{
    private static IWindow? _window;
    private static IInputContext? _input;
    private static GL _gl;
    private static VertexArrayObject _vao;
    private static VertexBufferObject _vbo;
    private static ElementBufferObject _ebo;
    private static Texture _texture;
    private static uint _shader;
    
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello World!");
        
        WindowOptions options = WindowOptions.Default with
        {
            Title = "Avalon",
            Size = new Vector2D<int>(1366, 768),
            VSync = false,
            VideoMode = new VideoMode(new Vector2D<int>(1920, 1080), 60),
            WindowBorder = WindowBorder.Fixed,
            WindowState = WindowState.Normal,
            API = GraphicsAPI.Default,
        };
        
        _window = Window.Create(options);

        Silk.NET.SDL.Vertex x = new Silk.NET.SDL.Vertex()
        {
            Color = new Silk.NET.SDL.Color(),
            Position = new FPoint(),
            TexCoord = new FPoint()
        };
        
        _window.Load += WindowOnLoad;
        _window.Update += WindowOnUpdate;
        _window.Render += WindowOnRender;
        _window.Closing += WindowOnClosing;
        _window.FramebufferResize += OnResize;
        _window.Run();
        
        Console.WriteLine("Goodbye World!");
    }
    
    private static void OnResize(Vector2D<int> size)
    {
        _gl.Viewport(0, 0, (uint) size.X, (uint) size.Y);
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

        _vao = new VertexArrayObject(_gl);
        _vao.Bind();

        _vbo = new VertexBufferObject(_gl, new[]
        {
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(0.5f, 0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 1.0f),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(0.5f, -0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 0.0f)
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(-0.5f, -0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 0.0f)
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(-0.5f, 0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 1.0f)
            }
        });
        
        _ebo = new ElementBufferObject(_gl, [
            0u, 1u, 3u,
            1u, 2u, 3u
        ]);
        
        const string vertexCode = @"
#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec4 aColor;
layout (location = 2) in vec2 aTextureCoord;

out vec2 frag_texCoords;
out vec4 frag_color;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
    frag_texCoords = aTextureCoord;
    frag_color = aColor;
}";
        const string fragmentCode = @"
#version 330 core

in vec2 frag_texCoords;
in vec4 frag_color;

out vec4 out_color;

uniform sampler2D uTexture;

void main()
{
    //out_color = frag_color;
    out_color = texture(uTexture, frag_texCoords);
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
        
        _vao.ConfigureAttributes(_vbo);
        
        _vao.Unbind();
        _vbo.Unbind();
        _ebo.Unbind();
        
        _texture = new Texture(_gl);
        _texture.Bind();
        _texture.Load("Logo.png");
        _texture.Unbind();
        
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }
    
    private static void WindowOnUpdate(double deltaTime)
    {
        
    }
    
    private static unsafe void WindowOnRender(double deltaTime)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        
        _gl.UseProgram(_shader);
        
        int location = _gl.GetUniformLocation(_shader, "uTexture");
        _gl.Uniform1(location, 0);
        
        _gl.ActiveTexture(TextureUnit.Texture0);
        
        _vao.Bind();
        _texture.Bind();
        
        _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*) 0);
        
        _texture.Unbind();
        _vao.Unbind();
        
        _gl.UseProgram(0);
    }
    
    private static void WindowOnClosing()
    {
        _vao.Unbind();
        _vbo.Unbind();
        _ebo.Unbind();
        
        _ebo.Dispose();
        Console.WriteLine("EBO Disposed!");
        _vbo.Dispose();
        Console.WriteLine("VBO Disposed!");
        _vao.Dispose();
        Console.WriteLine("VAO Disposed!");
        _texture.Dispose();
        Console.WriteLine("Texture Disposed!");
        
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
