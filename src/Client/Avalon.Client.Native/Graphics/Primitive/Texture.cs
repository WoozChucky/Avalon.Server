using Silk.NET.OpenGL;
using StbImageSharp;

namespace Avalon.Client.Native.Graphics.Primitive;

public class Texture
{
    public enum Name
    {
        Player,
        Enemy,
        Bullet,
        Background,
        Explosion,
    }
    
    private uint _handle;
    private GL _gl;
    
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    
    public Texture(GL gl, int width, int height)
    {
        _gl = gl;
        _handle = gl.GenTexture();
        Width = (uint)width;
        Height = (uint)height;
    }
    
    public void Bind()
    {
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }
    
    public void Unbind()
    {
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }
    
    public void Dispose()
    {
        _gl.DeleteTextures(1, in _handle);
    }
}