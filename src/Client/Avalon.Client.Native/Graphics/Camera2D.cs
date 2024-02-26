using System.Numerics;
using Avalon.Client.Native.Math;
using Silk.NET.OpenGL;

namespace Avalon.Client.Native.Graphics;

public class Camera2D
{
    private readonly float _viewportWidth;
    private readonly float _viewportHeight;
    private Matrix4x4 _viewMatrix;
    private Matrix4x4 _projectionMatrix;

    public Vector2 Position { get; private set; }
    public float Rotation { get; private set; }
    public float Zoom { get; private set; }

    public Camera2D(float viewportWidth, float viewportHeight)
    {
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        Reset();
    }
    
    public Matrix4x4 GetViewMatrix()
    {
        return _viewMatrix;
    }
    
    public Matrix4x4 GetProjectionMatrix()
    {
        return _projectionMatrix;
    }

    public void Reset()
    {
        Position = Vector2.Zero;
        Rotation = 0f;
        Zoom = 1f;
        UpdateMatrices();
    }

    public void Move(Vector2 amount)
    {
        Position += amount;
        UpdateMatrices();
    }

    public void Rotate(float amount)
    {
        Rotation += amount;
        UpdateMatrices();
    }

    public void ZoomIn(float amount)
    {
        Zoom += amount;
        UpdateMatrices();
    }

    public void ZoomOut(float amount)
    {
        Zoom -= amount;
        UpdateMatrices();
    }

    private void UpdateMatrices()
    {
        _viewMatrix = Matrix4x4.CreateTranslation(new Vector3(-Position.X, -Position.Y, 0)) *
                      Matrix4x4.CreateRotationZ(-Rotation) *
                      Matrix4x4.CreateScale(Zoom);

        _projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, _viewportWidth, _viewportHeight, 0, -1, 1);
    }
}
