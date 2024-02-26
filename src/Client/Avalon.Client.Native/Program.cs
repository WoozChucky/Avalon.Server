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
    private static ElementBufferObject<uint> _ebo;
    private static Texture _texture;
    private static Shader _shader;
    private static Transform[] _transforms = new Transform[4];
    
    private static readonly uint[] Indices =
    [
        0,  1,  2,  2,  3,  0, // Front face
        4,  5,  6,  6,  7,  4, // Back face
        8,  9, 10, 10, 11,  8, // Right face
        12, 13, 14, 14, 15, 12, // Left face
        16, 17, 18, 18, 19, 16, // Top face
        20, 21, 22, 22, 23, 20  // Bottom face
    ];
    
    private static Vector4D<float> Red => new Vector4D<float>(1.0f, 0.0f, 0.0f, 1.0f);
    private static Vector4D<float> Green => new Vector4D<float>(0.0f, 1.0f, 0.0f, 1.0f);
    private static Vector4D<float> Blue => new Vector4D<float>(0.0f, 0.0f, 1.0f, 1.0f);
    private static Vector4D<float> Yellow => new Vector4D<float>(1.0f, 1.0f, 0.0f, 1.0f);
    private static Vector4D<float> Cyan => new Vector4D<float>(0.0f, 0.5f, 0.5f, 1.0f);
    private static Vector4D<float> Magenta => new Vector4D<float>(0.5f, 0.0f, 0.5f, 1.0f);

    private static int Width => 1366;
    private static int Height => 768;
    
    private static IKeyboard _primaryKeyboard;
    
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
            PreferredDepthBufferBits = 24,
        };
        
        _window = Window.Create(options);
        
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
        _primaryKeyboard = _input.Keyboards.FirstOrDefault()!;
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
        
        float[] vertices =
        [
            // Front face
            -0.5f, -0.5f,  0.5f, 0.0f, 0.0f, // Bottom left
            0.5f, -0.5f,  0.5f, 1.0f, 0.0f, // Bottom right
            0.5f,  0.5f,  0.5f, 1.0f, 1.0f, // Top right
            -0.5f,  0.5f,  0.5f, 0.0f, 1.0f, // Top left

            // Back face
            0.5f, -0.5f, -0.5f, 0.0f, 0.0f,  // Bottom left
            -0.5f, -0.5f, -0.5f, 1.0f, 0.0f,  // Bottom right
            -0.5f,  0.5f, -0.5f, 1.0f, 1.0f,  // Top right
            0.5f,  0.5f, -0.5f, 0.0f, 1.0f,  // Top left

            // Right face
            0.5f, -0.5f,  0.5f, 0.0f, 0.0f, // Bottom back
            0.5f, -0.5f, -0.5f, 1.0f, 0.0f, // Bottom front
            0.5f,  0.5f, -0.5f, 1.0f, 1.0f, // Top front
            0.5f,  0.5f,  0.5f, 0.0f, 1.0f, // Top back

            // Left face
            -0.5f, -0.5f, -0.5f, 0.0f, 0.0f, // Bottom back
            -0.5f, -0.5f,  0.5f, 1.0f, 0.0f, // Bottom front
            -0.5f,  0.5f,  0.5f, 1.0f, 1.0f, // Top front
            -0.5f,  0.5f, -0.5f, 0.0f, 1.0f, // Top back

            // Top face
            -0.5f,  0.5f,  0.5f, 0.0f, 0.0f, // Front left
            0.5f,  0.5f,  0.5f, 1.0f, 0.0f, // Front right
            0.5f,  0.5f, -0.5f, 1.0f, 1.0f, // Back right
            -0.5f,  0.5f, -0.5f, 0.0f, 1.0f, // Back left

            // Bottom face
            -0.5f, -0.5f, -0.5f, 0.0f, 0.0f, // Front left
            0.5f, -0.5f, -0.5f, 1.0f, 0.0f, // Front right
            0.5f, -0.5f,  0.5f, 1.0f, 1.0f, // Back right
            -0.5f, -0.5f,  0.5f, 0.0f, 1.0f  // Back left
        ];
        
        _vbo = new VertexBufferObject(_gl, new[]
        {
            // Front face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(vertices[0], vertices[1], vertices[2]),
                Color = Red,
                TextureCoord = new Vector2D<float>(vertices[3], vertices[4]),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(vertices[5], vertices[6], vertices[7]),
                Color = Red,
                TextureCoord = new Vector2D<float>(vertices[8], vertices[9]),
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(vertices[10], vertices[11], vertices[12]),
                Color = Red,
                TextureCoord = new Vector2D<float>(vertices[13], vertices[14]),
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(vertices[15], vertices[16], vertices[17]),
                Color = Red,
                TextureCoord = new Vector2D<float>(vertices[18], vertices[19]),
            },
            // Back face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(vertices[20], vertices[21], vertices[22]),
                Color = Green,
                TextureCoord = new Vector2D<float>(vertices[23], vertices[24]),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(vertices[25], vertices[26], vertices[27]),
                Color = Green,
                TextureCoord = new Vector2D<float>(vertices[28], vertices[29]),
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(vertices[30], vertices[31], vertices[32]),
                Color = Green,
                TextureCoord = new Vector2D<float>(vertices[33], vertices[34]),
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(vertices[35], vertices[36], vertices[37]),
                Color = Green,
                TextureCoord = new Vector2D<float>(vertices[38], vertices[39]),
            },
            // Right face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(vertices[40], vertices[41], vertices[42]),
                Color = Blue,
                TextureCoord = new Vector2D<float>(vertices[43], vertices[44]),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(vertices[45], vertices[46], vertices[47]),
                Color = Blue,
                TextureCoord = new Vector2D<float>(vertices[48], vertices[49]),
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(vertices[50], vertices[51], vertices[52]),
                Color = Blue,
                TextureCoord = new Vector2D<float>(vertices[53], vertices[54]),
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(vertices[55], vertices[56], vertices[57]),
                Color = Blue,
                TextureCoord = new Vector2D<float>(vertices[58], vertices[59]),
            },
            // Left face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(vertices[60], vertices[61], vertices[62]),
                Color = Yellow,
                TextureCoord = new Vector2D<float>(vertices[63], vertices[64]),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(vertices[65], vertices[66], vertices[67]),
                Color = Yellow,
                TextureCoord = new Vector2D<float>(vertices[68], vertices[69]),
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(vertices[70], vertices[71], vertices[72]),
                Color = Yellow,
                TextureCoord = new Vector2D<float>(vertices[73], vertices[74]),
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(vertices[75], vertices[76], vertices[77]),
                Color = Yellow,
                TextureCoord = new Vector2D<float>(vertices[78], vertices[79]),
            },
            // Top face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(vertices[80], vertices[81], vertices[82]),
                Color = Cyan,
                TextureCoord = new Vector2D<float>(vertices[83], vertices[84]),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(vertices[85], vertices[86], vertices[87]),
                Color = Cyan,
                TextureCoord = new Vector2D<float>(vertices[88], vertices[89]),
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(vertices[90], vertices[91], vertices[92]),
                Color = Cyan,
                TextureCoord = new Vector2D<float>(vertices[93], vertices[94]),
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(vertices[95], vertices[96], vertices[97]),
                Color = Cyan,
                TextureCoord = new Vector2D<float>(vertices[98], vertices[99]),
            },
            // Bottom face
            new Vertex // Top Left
            {
                Position = new Vector3D<float>(vertices[100], vertices[101], vertices[102]),
                Color = Magenta,
                TextureCoord = new Vector2D<float>(vertices[103], vertices[104]),
            },
            new Vertex // Top Right
            {
                Position = new Vector3D<float>(vertices[105], vertices[106], vertices[107]),
                Color = Magenta,
                TextureCoord = new Vector2D<float>(vertices[108], vertices[109]),
            },
            new Vertex // Bottom Left
            {
                Position = new Vector3D<float>(vertices[110], vertices[111], vertices[112]),
                Color = Magenta,
                TextureCoord = new Vector2D<float>(vertices[113], vertices[114]),
            },
            new Vertex // Bottom Right
            {
                Position = new Vector3D<float>(vertices[115], vertices[116], vertices[117]),
                Color = Magenta,
                TextureCoord = new Vector2D<float>(vertices[118], vertices[119]),
            },
        });
        
        _ebo = new ElementBufferObject<uint>(_gl, Indices.AsSpan(), BufferTargetARB.ElementArrayBuffer);
        
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
//out vec3 fNormal;
//out vec3 fPos;

void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);

    //We want to know the fragment's position in World space, so we multiply ONLY by uModel and not uView or uProjection
    //fPos = vec3(uModel * vec4(vPos, 1.0));
    //The Normal needs to be in World space too, but needs to account for Scaling of the object
    //fNormal = mat3(transpose(inverse(uModel))) * vNormal;

    frag_texCoords = aTextureCoord;
    frag_color = aColor;
}";
        const string fragmentCode = @"
#version 330 core

in vec2 frag_texCoords;
in vec4 frag_color;

out vec4 out_color;

uniform sampler2D uTexture;

float near = 0.1; 
float far  = 100.0; 
  
float LinearizeDepth(float depth) 
{
    float z = depth * 2.0 - 1.0; // back to NDC 
    return (2.0 * near * far) / (far + near - z * (far - near));	
}

void main()
{
    //out_color = frag_color;
    float depth = LinearizeDepth(gl_FragCoord.z) / far;
    //out_color = texture(uTexture, frag_texCoords);
    out_color = frag_color * texture(uTexture, frag_texCoords);
    
}";
        
        _shader = new Shader(_gl);
        _shader.Create(vertexCode, fragmentCode);
        
        _vao.ConfigureAttributes(_vbo);
        
        _vao.Unbind();
        _vbo.Unbind();
        _ebo.Unbind();

        _texture = AssetsManager.Instance.GetTexture(Texture.Name.Player);
        
        Camera = new Camera3D(Vector3.UnitZ * 6, Vector3.UnitZ * -1, Vector3.UnitY, Width / Height);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
    }
    
    private static void WindowOnUpdate(double deltaTime)
    {
        var moveSpeed = 2.5f * (float) deltaTime;

        if (_primaryKeyboard.IsKeyPressed(Key.W))
        {
            //Move forwards
            Camera.Position += moveSpeed * Camera.Front;
        }
        if (_primaryKeyboard.IsKeyPressed(Key.S))
        {
            //Move backwards
            Camera.Position -= moveSpeed * Camera.Front;
        }
        if (_primaryKeyboard.IsKeyPressed(Key.A))
        {
            //Move left
            Camera.Position -= Vector3.Normalize(Vector3.Cross(Camera.Front, Camera.Up)) * moveSpeed;
        }
        if (_primaryKeyboard.IsKeyPressed(Key.D))
        {
            //Move right
            Camera.Position += Vector3.Normalize(Vector3.Cross(Camera.Front, Camera.Up)) * moveSpeed;
        }
        if (_primaryKeyboard.IsKeyPressed(Key.Space))
        {
            //Move up
            Camera.Position += moveSpeed * Camera.Up;
        }
        if (_primaryKeyboard.IsKeyPressed(Key.ShiftLeft))
        {
            //Move down
            Camera.Position -= moveSpeed * Camera.Up;
        }
    }
    
    private static unsafe void WindowOnRender(double deltaTime)
    {
        _gl.ClearDepth(1.0f);
        _gl.Clear((uint) (ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        
        // Part of the renderer
        _shader.Bind();
        _gl.ActiveTexture(TextureUnit.Texture0);
        
        // Part of the sprite batch
        _vao.Bind();
        _texture.Bind();
        
        // Part of the renderer
        _shader.SetUniform("uTexture", 0);
        
        _shader.SetUniform("uModel", Matrix4x4.CreateRotationY(MathHelper.DegreesToRadians(0f)));
        _shader.SetUniform("uView", Camera.GetViewMatrix());
        _shader.SetUniform("uProjection", Camera.GetProjectionMatrix());
        
        // Part of the sprite batch
        //_gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
        _gl.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, (void*) 0);
        
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
