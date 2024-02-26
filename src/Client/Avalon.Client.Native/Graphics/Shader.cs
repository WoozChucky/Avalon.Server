using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Avalon.Client.Native.Graphics;

public class Shader : IDisposable
{
    private GL _gl;
    
    private uint _program;
    private uint _vertexShader;
    private uint _fragmentShader;

    public Shader(GL gl)
    {
        _gl = gl;
    }
    
    public void Create(string vertexSource, string fragmentSource)
    {
        _vertexShader = _gl.CreateShader(GLEnum.VertexShader);
        _gl.ShaderSource(_vertexShader, vertexSource);
        _gl.CompileShader(_vertexShader);
        _gl.GetShader(_vertexShader, ShaderParameterName.CompileStatus, out var vStatus);
        if (vStatus != (int) GLEnum.True)
            throw new Exception("Vertex shader failed to compile: " + _gl.GetShaderInfoLog(_vertexShader));
        
        _fragmentShader = _gl.CreateShader(GLEnum.FragmentShader);
        _gl.ShaderSource(_fragmentShader, fragmentSource);
        _gl.CompileShader(_fragmentShader);
        _gl.GetShader(_fragmentShader, ShaderParameterName.CompileStatus, out var fStatus);
        if (fStatus != (int) GLEnum.True)
            throw new Exception("Fragment shader failed to compile: " + _gl.GetShaderInfoLog(_fragmentShader));
        
        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, _vertexShader);
        _gl.AttachShader(_program, _fragmentShader);
        _gl.LinkProgram(_program);
        
        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var lStatus);
        if (lStatus != (int) GLEnum.True)
            throw new Exception("Program failed to link: " + _gl.GetProgramInfoLog(_program));
        
        _gl.DetachShader(_program, _vertexShader);
        _gl.DetachShader(_program, _fragmentShader);
        _gl.DeleteShader(_vertexShader);
        _gl.DeleteShader(_fragmentShader);
    }
    
    public int GetUniformLocation(string name)
    {
        return _gl.GetUniformLocation(_program, name);
    }
    
    public void SetUniform(string name, int value)
    {
        int location = _gl.GetUniformLocation(_program, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
    }

    public unsafe void SetUniform(string name, Matrix4x4 value)
    {
        //A new overload has been created for setting a uniform so we can use the transform in our shader.
        int location = _gl.GetUniformLocation(_program, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.UniformMatrix4(location, 1, false, (float*) &value);
    }

    public void SetUniform(string name, float value)
    {
        int location = _gl.GetUniformLocation(_program, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
    }
    
    public void SetUniform(string name, Vector3D<float> value)
    {
        int location = _gl.GetUniformLocation(_program, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform3(location, value.X, value.Y, value.Z);
    }
    
    public void Bind()
    {
        _gl.UseProgram(_program);
    }
    
    public void Unbind()
    {
        _gl.UseProgram(0);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_program);
    }
}
