// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.Engine;

public class OctreeNode
{
    private readonly int _maxDepth;

    private readonly int _maxObjects;
    public bool IsEnabled = true;

    public OctreeNode(BoundingBox bounds, int maxObjects = 8, int maxDepth = 5)
    {
        Bounds = bounds;
        Objects = [];
        _maxObjects = maxObjects;
        _maxDepth = maxDepth;
    }

    public BoundingBox Bounds { get; }
    public List<SceneNode> Objects { get; }
    public OctreeNode?[] Children { get; } = new OctreeNode[8];
    public bool IsLeaf => Children[0] == null;

    public void Insert(SceneNode obj, int depth = 0)
    {
        // Ensure the object's bounding box is valid
        if (!obj.GlobalBoundingBox.HasValue)
        {
            Console.WriteLine($"Warning: Object '{obj.Name}' does not have a valid GlobalBoundingBox.");
            return; // Skip objects without a bounding box
        }

        BoundingBox objectBounds = obj.GlobalBoundingBox.Value;

        // If this is a leaf node, and we have space, add the object
        if ((IsLeaf && Objects.Count < _maxObjects) || depth >= _maxDepth)
        {
            Objects.Add(obj);
            return;
        }

        // Subdivide if necessary
        if (IsLeaf)
        {
            Subdivide();
        }

        // Add the object to all intersecting children
        bool addedToChild = false;
        foreach (OctreeNode child in Children)
        {
            if (child == null)
            {
                continue;
            }

            if (CheckCollisionBoxes(child.Bounds, objectBounds))
            {
                child.Insert(obj, depth + 1);
                addedToChild = true;
            }
        }

        // If the object doesn’t fit neatly into any child, keep it in this node
        if (!addedToChild)
        {
            // Objects.Add(obj);
            Console.WriteLine($"Warning: Object '{obj.Name}' does not fit neatly into any child node.");
        }
    }

    public void Subdivide()
    {
        Vector3 size = (Bounds.Max - Bounds.Min) / 2;
        Vector3 center = Bounds.Min + size;

        Children[0] = new OctreeNode(new BoundingBox(Bounds.Min, center), _maxObjects, _maxDepth); // Bottom-Left-Front
        Children[1] =
            new OctreeNode(
                new BoundingBox(new Vector3(center.X, Bounds.Min.Y, Bounds.Min.Z),
                    new Vector3(Bounds.Max.X, center.Y, center.Z)), _maxObjects, _maxDepth); // Bottom-Right-Front
        Children[2] =
            new OctreeNode(
                new BoundingBox(new Vector3(Bounds.Min.X, Bounds.Min.Y, center.Z),
                    new Vector3(center.X, center.Y, Bounds.Max.Z)), _maxObjects, _maxDepth); // Bottom-Left-Back
        Children[3] = new OctreeNode(new BoundingBox(new Vector3(center.X, Bounds.Min.Y, center.Z), Bounds.Max),
            _maxObjects, _maxDepth); // Bottom-Right-Back
        Children[4] =
            new OctreeNode(
                new BoundingBox(new Vector3(Bounds.Min.X, center.Y, Bounds.Min.Z),
                    new Vector3(center.X, Bounds.Max.Y, center.Z)), _maxObjects, _maxDepth); // Top-Left-Front
        Children[5] =
            new OctreeNode(
                new BoundingBox(new Vector3(center.X, center.Y, Bounds.Min.Z),
                    new Vector3(Bounds.Max.X, Bounds.Max.Y, center.Z)), _maxObjects, _maxDepth); // Top-Right-Front
        Children[6] =
            new OctreeNode(
                new BoundingBox(new Vector3(Bounds.Min.X, center.Y, center.Z),
                    new Vector3(center.X, Bounds.Max.Y, Bounds.Max.Z)), _maxObjects, _maxDepth); // Top-Left-Back
        Children[7] = new OctreeNode(new BoundingBox(center, Bounds.Max), _maxObjects, _maxDepth); // Top-Right-Back
    }

    public void ToggleNode()
    {
        IsEnabled = !IsEnabled;

        foreach (SceneNode sceneNode in Objects)
        {
            sceneNode.IsEnabled = IsEnabled;
        }

        if (IsLeaf)
        {
            return;
        }

        foreach (OctreeNode child in Children)
        {
            child?.ToggleNode();
        }
    }
}
