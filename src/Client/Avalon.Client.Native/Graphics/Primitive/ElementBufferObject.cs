using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Avalon.Client.Native.Graphics;

public class ElementBufferObject : IDisposable
{
    private uint _eboHandle;
    private uint[] _indices;
    private IntPtr _buffer;
    private GL _gl;
    
    private int Size => _indices.Length * sizeof(uint);

    public unsafe ElementBufferObject(GL gl, uint[] indices)
    {
        _gl = gl;
        _indices = indices;
        gl.GenBuffers(1, out _eboHandle);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, _eboHandle);
        fixed (uint* buf = indices)
            gl.BufferData(GLEnum.ElementArrayBuffer, (uint)Size, buf/*_indices.AsSpan().GetPinnableReference()*/, GLEnum.StaticDraw);
    }

    public void Bind()
    {
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _eboHandle);
    }

    public void Unbind()
    {
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
    }
    
    private unsafe uint* GetMemory()
    {
        if (_buffer == IntPtr.Zero)
        {
            _buffer = Marshal.AllocHGlobal(Size);
            
            var buffer = (uint*) _buffer;
            for (var i = 0; i < _indices.Length; i++)
            {
                buffer[i] = _indices[i];
            }
            return buffer;
        }

        return (uint*) _buffer;
    }

    public void Dispose()
    {
        _gl.DeleteBuffers(1, in _eboHandle);
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
        }
    }
}