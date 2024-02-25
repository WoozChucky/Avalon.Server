using Silk.NET.OpenGL;
using StbImageSharp;

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Avalon.Client.Native.Graphics;

public class Texture
{
    private uint _handle;
    private GL _gl;
    
    public Texture(GL gl)
    {
        _gl = gl;
        _handle = gl.GenTexture();
    }
    
    public void Bind()
    {
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }
    
    public void Unbind()
    {
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }
    
    public unsafe void Load(string filePath)
    {
        var buffer = File.ReadAllBytes(filePath);
        StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(1);
        var image = ImageResult.FromMemory(buffer, ColorComponents.RedGreenBlueAlpha);
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
    }
    
    public void Dispose()
    {
        _gl.DeleteTextures(1, in _handle);
    }
}