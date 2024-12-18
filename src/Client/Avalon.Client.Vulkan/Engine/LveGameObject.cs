// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Systems.PointLight;

namespace Avalon.Client.Vulkan.Engine;

public class LveGameObject
{
    private static uint currentId;

    private LveGameObject(uint id)
    {
        Id = id;
        Transform = new TransformComponent();
    }

    // Id prop
    public uint Id { get; }

    // other props
    public LveModel? Model { get; set; } = null;
    public PointLightComponent? PointLight { get; set; }
    public Vector4 Color { get; set; } = new(1.0f);
    public TransformComponent Transform { get; set; }

    public static LveGameObject MakePointLight(float intensity, float radius, Vector4 color)
    {
        LveGameObject gameObj = CreateGameObject();
        gameObj.Color = color;
        gameObj.Transform.Scale.X = radius;
        gameObj.PointLight = new PointLightComponent(intensity);
        return gameObj;
    }

    public static LveGameObject CreateGameObject()
    {
        currentId++;
        return new LveGameObject(currentId);
    }

    public static uint GetNextID()
    {
        currentId++;
        return currentId;
    }


    public class TransformComponent
    {
        public Vector3 Rotation;
        public Vector3 Scale;
        public Vector3 Translation;

        public TransformComponent()
        {
            Translation = Vector3.Zero;
            Rotation = Vector3.Zero;
            Scale = Vector3.One;
        }


        public Matrix4x4 Mat4Old()
        {
            Matrix4x4 matTranslate = Matrix4x4.CreateTranslation(Translation);
            Matrix4x4 matScale = Matrix4x4.CreateScale(Scale);
            Matrix4x4 matRot = Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z);
            return matScale * matRot * matTranslate;
        }

        public Matrix4x4 Mat4()
        {
            float c3 = MathF.Cos(Rotation.Z);
            float s3 = MathF.Sin(Rotation.Z);
            float c2 = MathF.Cos(Rotation.X);
            float s2 = MathF.Sin(Rotation.X);
            float c1 = MathF.Cos(Rotation.Y);
            float s1 = MathF.Sin(Rotation.Y);

            return new Matrix4x4(
                Scale.X * (c1 * c3 + s1 * s2 * s3),
                Scale.X * (c2 * s3),
                Scale.X * (c1 * s2 * s3 - c3 * s1),
                0.0f,
                Scale.Y * (c3 * s1 * s2 - c1 * s3),
                Scale.Y * (c2 * c3),
                Scale.Y * (c1 * c3 * s2 + s1 * s3),
                0.0f,
                Scale.Z * (c2 * s1),
                Scale.Z * -s2,
                Scale.Z * (c1 * c2),
                0.0f,
                Translation.X, Translation.Y, Translation.Z, 1.0f
            );
        }

        public Matrix4x4 NormalMatrix()
        {
            float c3 = MathF.Cos(Rotation.Z);
            float s3 = MathF.Sin(Rotation.Z);
            float c2 = MathF.Cos(Rotation.X);
            float s2 = MathF.Sin(Rotation.X);
            float c1 = MathF.Cos(Rotation.Y);
            float s1 = MathF.Sin(Rotation.Y);

            Vector3 invScale = new(1f / Scale.X, 1f / Scale.Y, 1f / Scale.Z);

            return new Matrix4x4(
                invScale.X * (c1 * c3 + s1 * s2 * s3),
                invScale.X * (c2 * s3),
                invScale.X * (c1 * s2 * s3 - c3 * s1),
                0.0f,
                invScale.Y * (c3 * s1 * s2 - c1 * s3),
                invScale.Y * (c2 * c3),
                invScale.Y * (c1 * c3 * s2 + s1 * s3),
                0.0f,
                invScale.Z * (c2 * s1),
                invScale.Z * -s2,
                invScale.Z * (c1 * c2),
                0.0f,
                0.0f, 0.0f, 0.0f, 1.0f
            );
        }
    }
}
