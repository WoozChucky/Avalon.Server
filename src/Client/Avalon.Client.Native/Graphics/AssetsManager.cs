using Silk.NET.OpenGL;
using StbImageSharp;
using Texture = Avalon.Client.Native.Graphics.Primitive.Texture;

namespace Avalon.Client.Native.Graphics;

public class AssetsManager : IDisposable
{
    public static AssetsManager Instance { get; } = new AssetsManager();
    
    private readonly Dictionary<Texture.Name, Texture> _textures = new();
    private readonly Dictionary<Shader.Name, string> _shaders = new();
    private GL? _gl;
    
    static AssetsManager()
    {
        StbImage.stbi_set_flip_vertically_on_load(1);
    }

    public void Initialize(GL gl)
    {
        _gl = gl;
        // Preload
        LoadTexture(Texture.Name.Player);
        LoadTexture(Texture.Name.Background);
        
        LoadShader(Shader.Name.DefaultVertex);
        LoadShader(Shader.Name.DefaultFragment);
    }
    
    public string GetShader(Shader.Name name)
    {
        var item = _shaders.FirstOrDefault(x => x.Key == name);
        if (item.Value == null)
        {
            var loaded = LoadShader(name);
            _shaders.Add(name, loaded);
            return loaded;
        }
        return item.Value;
    }

    private string LoadShader(Shader.Name name)
    {
        return name switch
        {
            Shader.Name.DefaultVertex => File.ReadAllText("Graphics/Shaders/shader.vert"),
            Shader.Name.DefaultFragment => File.ReadAllText("Graphics/Shaders/shader.frag"),
            _ => throw new InvalidOperationException("Shader not found.")
        };
    }

    public Texture GetTexture(Texture.Name name)
    {
        var item = _textures.FirstOrDefault(x => x.Key == name);
        if (item.Value == null)
        {
            var loaded = LoadTexture(name);
            _textures.Add(name, loaded);
            return loaded;
        }
        return item.Value;
    }
    
    public void Dispose()
    {
        foreach (var texture in _textures)
        {
            texture.Value.Dispose();
        }
        _textures.Clear();
    }
    
    private unsafe Texture LoadTexture(Texture.Name name)
    {
        var imageBuffer = File.ReadAllBytes(GetFilePath(name));
        
        var image = ImageResult.FromMemory(imageBuffer, ColorComponents.RedGreenBlueAlpha);
        
        var texture = new Texture(
            _gl ?? throw new InvalidOperationException("GL context not set"), 
            image.Width, 
            image.Height
        );
        
        texture.Bind();
        
        fixed (void* buf = image.Data)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)image.Width,
                (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, buf);
        }
        
        // Set texture parameters
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int) GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int) GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 8);
        
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        
        texture.Unbind();
        return texture;
    }

    private string GetFilePath(Texture.Name name)
    {
        return name switch
        {
            Texture.Name.Player => "Logo.png",
            Texture.Name.Enemy => "silk.png",
            Texture.Name.Bullet => "yellow.jpg",
            Texture.Name.Background => "2.png",
            Texture.Name.Explosion => "expr",
            _ => throw new InvalidOperationException("Texture not found.")
        };
    }

    
}
