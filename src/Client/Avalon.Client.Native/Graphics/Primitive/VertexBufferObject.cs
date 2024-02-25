using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Avalon.Client.Native.Graphics;

public class VertexBufferObject : IDisposable
{
    private uint _vboHandle;
    private Vertex[] _vertices;
    private IntPtr _buffer;
    private GL _gl;
    
    private int Size => _vertices.Length * Vertex.TotalSizeInBytes;
    
    public unsafe VertexBufferObject(GL gl, Vertex[] vertices)
    {
        _vertices = vertices;
        _gl = gl;
        gl.GenBuffers(1, out _vboHandle);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
        gl.BufferData(GLEnum.ArrayBuffer, (uint)Size, GetMemory(), GLEnum.StaticDraw);
    }
    
    public void Bind()
    {
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
    }

    public void Unbind()
    {
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }
    
    private unsafe float* GetMemory()
    {
        if (_buffer == IntPtr.Zero)
        {
            _buffer = Marshal.AllocHGlobal(Size);
            
            var buffer = (float*) _buffer;
            for (var i = 0; i < _vertices.Length; i++)
            {
                buffer[i * 9] = _vertices[i].Position.X;
                buffer[i * 9 + 1] = _vertices[i].Position.Y;
                buffer[i * 9 + 2] = _vertices[i].Position.Z;
                
                buffer[i * 9 + 3] = _vertices[i].Color.X;
                buffer[i * 9 + 4] = _vertices[i].Color.Y;
                buffer[i * 9 + 5] = _vertices[i].Color.Z;
                buffer[i * 9 + 6] = _vertices[i].Color.W;
                    
                buffer[i * 9 + 7] = _vertices[i].TextureCoord.X;
                buffer[i * 9 + 8] = _vertices[i].TextureCoord.Y;
            }
            return buffer;
        }

        return (float*) _buffer;
    }
    
    private void Dispose(GL gl)
    {
        gl.DeleteBuffers(1, in _vboHandle);
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
        }
    }

    public void Dispose()
    {
        Dispose(_gl);
    }
}
