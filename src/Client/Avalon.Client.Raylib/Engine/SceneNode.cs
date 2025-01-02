// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.Engine;

public class SceneNode
{
    private Vector3 _position;
    public bool IsEnabled = true;
    public string Name { get; set; }

    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            GlobalBoundingBox = GlobalBoundingBox.HasValue
                ? new BoundingBox {Min = GlobalBoundingBox.Value.Min + value, Max = GlobalBoundingBox.Value.Max + value}
                : null;
        }
    }

    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;

    public BoundingBox? GlobalBoundingBox { get; private set; }
    public List<BoundingBox> MeshBoundingBoxes { get; } = [];
    public BoundingSphere? BoundingSphere { get; private set; }

    public SceneNode? Parent { get; private set; }
    public List<SceneNode> Children { get; } = [];

    public Matrix4x4 LocalTransform
    {
        get
        {
            Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(Scale);
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(Position);

            return scaleMatrix * rotationMatrix * translationMatrix;
        }
    }

    public Matrix4x4 GlobalTransform
    {
        get
        {
            if (Parent == null)
            {
                return LocalTransform;
            }

            return LocalTransform * Parent.GlobalTransform;
        }
    }

    public bool DrawBounds { get; set; } = false;

    public void AddChild(SceneNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void RemoveChild(SceneNode child)
    {
        child.Parent = null;
        Children.Remove(child);
    }

    // Set Bounding Volumes
    public unsafe void SetBoundingVolumes(Model model)
    {
        MeshBoundingBoxes.Clear();

        for (int i = 0; i < model.MeshCount; i++)
        {
            MeshBoundingBoxes.Add(CalculateBoundingBoxForSingleMesh(model.Meshes[i]));
        }

        GlobalBoundingBox = MergeBoundingBoxes(MeshBoundingBoxes);
        BoundingSphere = CalculateBoundingSphere(GlobalBoundingBox.Value);
    }

    public void SetBoundingVolumes(BoundingBox boundingBox)
    {
        GlobalBoundingBox = boundingBox;
        BoundingSphere = CalculateBoundingSphere(boundingBox);
    }

    public void SetBoundingVolumes(Vector3 size)
    {
        BoundingBox box = new() {Min = -size / 2, Max = size / 2};
        SetBoundingVolumes(box);
    }

    private static BoundingBox CalculateBoundingBoxForSingleMesh(Mesh mesh)
    {
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);

        unsafe
        {
            int vertexCount = mesh.VertexCount;
            float* vertices = mesh.Vertices;

            for (int i = 0; i < vertexCount * 3; i += 3)
            {
                Vector3 vertex = new(vertices[i], vertices[i + 1], vertices[i + 2]);

                // Update min and max vectors
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
        }

        return new BoundingBox {Min = min, Max = max};
    }

    private static BoundingBox MergeBoundingBoxes(List<BoundingBox> boundingBoxes)
    {
        Vector3 globalMin = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 globalMax = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (BoundingBox box in boundingBoxes)
        {
            globalMin = Vector3.Min(globalMin, box.Min);
            globalMax = Vector3.Max(globalMax, box.Max);
        }

        return new BoundingBox {Min = globalMin, Max = globalMax};
    }

    private BoundingSphere CalculateBoundingSphere(BoundingBox box)
    {
        Vector3 center = Vector3.Lerp(box.Min, box.Max, 0.5f);
        float radius = Vector3.Distance(center, box.Max);
        return new BoundingSphere {Center = center, Radius = radius};
    }
}
