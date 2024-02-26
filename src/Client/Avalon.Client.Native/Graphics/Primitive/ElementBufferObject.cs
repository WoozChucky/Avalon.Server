using Silk.NET.OpenGL;

namespace Avalon.Client.Native.Graphics.Primitive;

public class ElementBufferObject<TDataType> : IDisposable
    where TDataType : unmanaged
{
    private readonly uint _eboHandle;
    private readonly GL _gl;
    private readonly BufferTargetARB _bufferType;

    public unsafe ElementBufferObject(GL gl, Span<TDataType> data, BufferTargetARB bufferType)
    {
        _gl = gl;
        _bufferType = bufferType;
        gl.GenBuffers(1, out _eboHandle);
        gl.BindBuffer(bufferType, _eboHandle);
        fixed (void* buf = data)
            gl.BufferData(_bufferType, (uint)(data.Length * sizeof(TDataType)), buf, GLEnum.StaticDraw);
    }

    public void Bind()
    {
        _gl.BindBuffer(_bufferType, _eboHandle);
    }

    public void Unbind()
    {
        _gl.BindBuffer(_bufferType, 0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_eboHandle);
    }
}