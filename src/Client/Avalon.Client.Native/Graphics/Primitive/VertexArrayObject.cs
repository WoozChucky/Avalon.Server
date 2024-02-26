using Silk.NET.OpenGL;

namespace Avalon.Client.Native.Graphics.Primitive;

public class VertexArrayObject : IDisposable
{
    private uint vaoHandle;
    private GL _gl;

    public VertexArrayObject(GL gl)
    {
        _gl = gl;
        gl.GenVertexArrays(1, out vaoHandle);
    }

    public void Bind()
    {
        _gl.BindVertexArray(vaoHandle);
    }

    public void Unbind()
    {
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArrays(1, in vaoHandle);
    }

    // Method to configure vertex attribute pointers
    public unsafe void ConfigureAttributes(VertexBufferObject vbo)
    {
        Bind();
        vbo.Bind();
        
        const uint positionLoc = 0;
        _gl.EnableVertexAttribArray(positionLoc);
        _gl.VertexAttribPointer(positionLoc, Vertex.PositionSize, VertexAttribPointerType.Float, false, (uint)Vertex.TotalSizeInBytes, (void *)Vertex.PositionOffset);
        
        const uint colorLoc = 1;
        _gl.EnableVertexAttribArray(colorLoc);
        _gl.VertexAttribPointer(colorLoc, Vertex.ColorSize, VertexAttribPointerType.Float, false, (uint)Vertex.TotalSizeInBytes, (void*)Vertex.ColorOffset);
        
        const uint texCoordLoc = 2;
        _gl.EnableVertexAttribArray(texCoordLoc);
        _gl.VertexAttribPointer(texCoordLoc, Vertex.TextureCoordSize, VertexAttribPointerType.Float, false, (uint)Vertex.TotalSizeInBytes, (void*)Vertex.TextureCoordOffset);
        
        Unbind();
        vbo.Unbind();
    }
}
