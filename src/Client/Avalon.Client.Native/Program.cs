using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalon.Client.Native.Graphics;
using Avalon.Client.Native.Graphics.Primitive;
using Avalon.Client.Native.Math;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using Color = System.Drawing.Color;
using Shader = Avalon.Client.Native.Graphics.Shader;
using Texture = Avalon.Client.Native.Graphics.Primitive.Texture;
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
    private static Shader _shader;
    private static Transform[] _transforms = new Transform[4];

    private static int Width => 1366;
    private static int Height => 768;
    
    private static IKeyboard primaryKeyboard;
    
    //Setup the camera's location, and relative up and right directions
    private static Camera3D Camera;

    //Used to track change in mouse movement to allow for moving of the Camera
    private static Vector2 LastMousePosition;
    
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello World!");
        
        WindowOptions options = WindowOptions.Default with
        {
            Title = "Avalon",
            Size = new Vector2D<int>(Width, Height),
            VSync = false,
            VideoMode = new VideoMode(new Vector2D<int>(Width, Height), 60),
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
        primaryKeyboard = _input.Keyboards.FirstOrDefault()!;
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
        
        AssetsManager.Instance.Initialize(_gl);

        _vao = new VertexArrayObject(_gl);
        _vao.Bind();

        _vbo = new VertexBufferObject(_gl, new[]
        {
            // Front face
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
            },
            // Back face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(0.5f, 0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 1.0f),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(0.5f, -0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 0.0f)
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(-0.5f, -0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 0.0f)
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(-0.5f, 0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 1.0f)
            },
            // Top face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(0.5f, 0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 1.0f),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(0.5f, 0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 0.0f)
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(-0.5f, 0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 0.0f)
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(-0.5f, 0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 1.0f)
            },
            // Bottom face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(0.5f, -0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 1.0f),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(0.5f, -0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 0.0f)
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(-0.5f, -0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 0.0f)
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(-0.5f, -0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 1.0f)
            },
            // Left face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(-0.5f, 0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 1.0f),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(-0.5f, 0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 0.0f)
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(-0.5f, -0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 0.0f)
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(-0.5f, -0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 1.0f)
            },
            // Right face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(0.5f, 0.5f, 0.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 1.0f),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(0.5f, 0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 1.0f, 0.0f, 1.0f),
                TextureCoord = new Vector2D<float>(1.0f, 0.0f)
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(0.5f, -0.5f, -1.0f),
                Color = new Vector4D<float>(1.0f, 0.0f, 1.0f, 1.0f),
                TextureCoord = new Vector2D<float>(0.0f, 0.0f)
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(0.5f, -0.5f, 0.0f),
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

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec2 frag_texCoords;
out vec4 frag_color;

void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
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
        
        _shader = new Shader(_gl);
        _shader.Create(vertexCode, fragmentCode);
        
        _vao.ConfigureAttributes(_vbo);
        
        _vao.Unbind();
        _vbo.Unbind();
        _ebo.Unbind();

        _texture = AssetsManager.Instance.GetTexture(Texture.Name.Enemy);
        
        Camera = new Camera3D(Vector3.UnitZ * 6, Vector3.UnitZ * -1, Vector3.UnitY, Width / Height);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }
    
    private static void WindowOnUpdate(double deltaTime)
    {
        var moveSpeed = 2.5f * (float) deltaTime;

        if (primaryKeyboard.IsKeyPressed(Key.W))
        {
            //Move forwards
            Camera.Position += moveSpeed * Camera.Front;
        }
        if (primaryKeyboard.IsKeyPressed(Key.S))
        {
            //Move backwards
            Camera.Position -= moveSpeed * Camera.Front;
        }
        if (primaryKeyboard.IsKeyPressed(Key.A))
        {
            //Move left
            Camera.Position -= Vector3.Normalize(Vector3.Cross(Camera.Front, Camera.Up)) * moveSpeed;
        }
        if (primaryKeyboard.IsKeyPressed(Key.D))
        {
            //Move right
            Camera.Position += Vector3.Normalize(Vector3.Cross(Camera.Front, Camera.Up)) * moveSpeed;
        }
    }
    
    private static unsafe void WindowOnRender(double deltaTime)
    {
        _gl.Enable(EnableCap.DepthTest);
        _gl.Clear((uint) (ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        
        // Part of the renderer
        _shader.Bind();
        _gl.ActiveTexture(TextureUnit.Texture0);
        
        // Part of the sprite batch
        _vao.Bind();
        _texture.Bind();
        
        // Part of the renderer
        _shader.SetUniform("uTexture", 0);
        
        //Use elapsed time to convert to radians to allow our cube to rotate over time
        var difference = (float) (_window.Time * 100);
        
        _shader.SetUniform("uModel", Matrix4x4.CreateRotationY(MathHelper.DegreesToRadians(25f)));
        _shader.SetUniform("uView", Camera.GetViewMatrix());
        _shader.SetUniform("uProjection", Camera.GetProjectionMatrix());
        
        // Part of the sprite batch
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
        //_gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*) 0);
        
        // Part of the sprite batch
        _texture.Unbind();
        _vao.Unbind();
        
        // Part of the renderer
        _shader.Unbind();
    }
    
    private static void WindowOnClosing()
    {
        _vao.Unbind();
        _vbo.Unbind();
        _ebo.Unbind();
        _shader.Unbind();
        
        _ebo.Dispose();
        Console.WriteLine("EBO Disposed!");
        _vbo.Dispose();
        Console.WriteLine("VBO Disposed!");
        _vao.Dispose();
        Console.WriteLine("VAO Disposed!");
        AssetsManager.Instance.Dispose();
        Console.WriteLine("Textures Disposed!");
        _shader.Dispose();
        Console.WriteLine("Shader Disposed!");
        _input!.Dispose();
        Console.WriteLine("Input Disposed!");
    }
    
    private static void OnMouseScroll(IMouse arg1, ScrollWheel scrollWheel)
    {
        Camera.ModifyZoom(scrollWheel.Y);
    }

    private static void OnMouseDoubleClick(IMouse arg1, MouseButton arg2, Vector2 arg3)
    {
        Console.WriteLine("Mouse Double Clicked!");
    }

    private static void OnMouseClick(IMouse arg1, MouseButton arg2, Vector2 arg3)
    {
        Console.WriteLine("Mouse Clicked!");
    }

    private static void OnMouseMove(IMouse arg1, Vector2 position)
    {
        var lookSensitivity = 0.1f;
        if (LastMousePosition == default) { LastMousePosition = position; }
        else
        {
            var xOffset = (position.X - LastMousePosition.X) * lookSensitivity;
            var yOffset = (position.Y - LastMousePosition.Y) * lookSensitivity;
            LastMousePosition = position;

            Camera.ModifyDirection(xOffset, yOffset);
        }
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
