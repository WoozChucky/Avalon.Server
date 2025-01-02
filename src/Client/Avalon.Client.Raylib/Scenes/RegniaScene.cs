// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;
using Avalon.Client.Engine;
using IconFonts;
using ImGuiNET;
using rlImGui_cs;

namespace Avalon.Client.Scenes;

public class RegniaScene : IScene
{
    private readonly CameraThirdPerson _camera = new();
    private Frustum _frustum;
    private Model _houseModel;
    private uint _objectsRendered;
    private OctreeNode? _octree;
    private bool _pause;
    private SceneNode? _rootNode;
    private bool _showingCursor;
    private bool _showingImGui;
    private bool _showNodeBounds = true;
    private bool _showObjectBounds;

    public void Setup()
    {
        _camera.Setup(45, new Vector3(0.0f, 10.0f, 0.0f), new Vector3(100, 100, 100));
        _camera.ViewAngles.x = -2.35f;
        _camera.ViewAngles.y = -0.25f;

        _houseModel = LoadModel("bootybay_house1.obj"); // Load OBJ model

        _rootNode = new SceneNode {Name = "Root"};

        BoundingBox worldBounds = new(new Vector3(-100, -100, -100), new Vector3(100, 100, 100));
        _octree = new OctreeNode(worldBounds);

        const int houseSize = 25;
        const int houseSpacing = 10;
        const int housesPerRow = 10;
        const int housePerColumn = 10;

        for (int row = 0; row < housePerColumn; row++)
        {
            for (int col = 0; col < housesPerRow; col++)
            {
                SceneNode newHouseNode = new() {Name = $"House[{row}][{col}]"};
                newHouseNode.SetBoundingVolumes(_houseModel);
                // newHouseNode.SetBoundingVolumes(new Vector3(11, 6, 11));
                newHouseNode.Position = new Vector3(col * houseSize + houseSpacing - 100, 0,
                    row * houseSize + houseSpacing - 100);
                _rootNode.AddChild(newHouseNode);
                _octree.Insert(newHouseNode);
            }
        }
    }

    public void Update()
    {
        if (IsKeyPressed(KeyboardKey.P))
        {
            _pause = !_pause;
        }

        if (IsKeyPressed(KeyboardKey.C))
        {
            _showingCursor = !_showingCursor;
            if (_showingCursor)
            {
                EnableCursor();
            }
            else
            {
                DisableCursor();
            }
        }

        if (IsKeyPressed(KeyboardKey.I))
        {
            _showingImGui = !_showingImGui;
        }

        _camera.Update();
        _frustum = _camera.CalculateFrustum(GetScreenWidth(), GetScreenHeight());
    }

    public void Render()
    {
        _objectsRendered = 0;

        List<Matrix4x4> houseTransforms = [];
        HashSet<SceneNode> processedObjects = [];

        BeginDrawing();

        ClearBackground(Color.DarkGray);

        _camera.BeginMode3D();

        TraverseAndRender(_octree, processedObjects);

        //DrawModelInstanced(_houseModel, houseTransforms);

        DrawGrid(2000, 1.0f); // Draw a grid

        _camera.EndMode3D();

        DrawFPS(10, 10);
        DrawText($"{GetFrameTime():#,0.000} ms", 10, 30, 20, Color.Lime);
        DrawText($"{GetTime():F} sec", 10, 50, 20, Color.Lime);
        DrawText($"{_objectsRendered} rendered", 10, 70, 20, Color.Black);

        if (_showingImGui)
        {
            rlImGui.Begin();

            if (ImGui.Begin("Octree Hierarquy"))
            {
                RenderOctreeVisualization();
            }

            ImGui.End();

            rlImGui.End();
        }

        EndDrawing();
    }

    public void Unload() => UnloadModel(_houseModel);

    private void RenderOctreeVisualization()
    {
        ImGui.TextUnformatted(FontAwesome6.Book);
        ImGui.Separator();

        // Display statistics
        ImGui.Text($"Nodes: {GetNodeCount(_octree)}");
        ImGui.Text($"Objects: {GetObjectCount(_octree)}");
        ImGui.Text($"Max Depth: {GetMaxDepth(_octree)}");
        ImGui.Separator();

        // Render the Octree hierarchy
        RenderOctreeNode(_octree);

        ImGui.Separator();

        // Debug options
        ImGui.Text("Debug Options:");
        ImGui.Checkbox("Show Node Bounds", ref _showNodeBounds);
        ImGui.Checkbox("Show Object Bounds", ref _showObjectBounds);
    }

    private int GetMaxDepth(OctreeNode? node)
    {
        if (node == null)
        {
            return 0;
        }

        // If the node is a leaf, its depth is 1
        if (node.IsLeaf)
        {
            return 1;
        }

        // Calculate the depth of each child node
        int maxChildDepth = 0;
        foreach (OctreeNode? child in node.Children)
        {
            if (child != null)
            {
                maxChildDepth = Math.Max(maxChildDepth, GetMaxDepth(child));
            }
        }

        // Return 1 (current node) + maximum child depth
        return 1 + maxChildDepth;
    }

    private int GetNodeCount(OctreeNode? node)
    {
        if (node == null)
        {
            return 0;
        }

        int count = 1; // Count this node
        if (!node.IsLeaf)
        {
            foreach (OctreeNode? child in node.Children)
            {
                if (child != null)
                {
                    count += GetNodeCount(child);
                }
            }
        }

        return count;
    }

    private int GetObjectCount(OctreeNode? node)
    {
        if (node == null)
        {
            return 0;
        }

        int count = node.Objects.Count;
        if (!node.IsLeaf)
        {
            foreach (OctreeNode? child in node.Children)
            {
                if (child != null)
                {
                    count += GetObjectCount(child);
                }
            }
        }

        return count;
    }

    private void RenderOctreeNode(OctreeNode? node)
    {
        if (node == null)
        {
            return;
        }

        string? nodeStatus = node.IsEnabled ? FontAwesome6.E : FontAwesome6.D;

        // Create a tree node for this Octree node
        if (ImGui.TreeNode($"[{nodeStatus}] Node: {node.Bounds.Min} - {node.Bounds.Max}"))
        {
            if (ImGui.Button("Enable/Disable"))
            {
                node.ToggleNode();
            }

            // Highlight the node if hovered
            if (ImGui.IsItemHovered())
            {
                DrawBoundingBox(node.Bounds, Color.Yellow); // Highlight in the scene
            }

            foreach (SceneNode obj in node.Objects)
            {
                if (!node.IsEnabled)
                {
                    continue;
                }

                string? objStatus = obj.IsEnabled ? FontAwesome6.E : FontAwesome6.D;

                ImGui.BulletText($"[{objStatus}] {obj.Name}, Position: {obj.Position}");

                if (ImGui.IsItemClicked())
                {
                    obj.IsEnabled = !obj.IsEnabled;
                }

                if (ImGui.IsItemHovered())
                {
                    obj.DrawBounds = true;
                }
                else
                {
                    obj.DrawBounds = false;
                }
            }

            if (!node.IsLeaf)
            {
                foreach (OctreeNode? child in node.Children)
                {
                    if (child != null)
                    {
                        RenderOctreeNode(child);
                    }
                }
            }

            ImGui.TreePop();
        }
    }

    private void TraverseAndRender(OctreeNode? node, HashSet<SceneNode> processedObjects)
    {
        if (node == null || !node.IsEnabled)
        {
            return;
        }

        // Draw the node's bounding box
        if (_showNodeBounds)
        {
            DrawBoundingBox(node.Bounds, Color.Blue);
        }

        // Skip nodes outside the frustum
        if (!CheckCollisionBoxFrustum(node.Bounds, _frustum))
        {
            return;
        }

        // Render objects in this node
        foreach (SceneNode obj in node.Objects)
        {
            if (!processedObjects.Contains(obj) && obj.GlobalBoundingBox.HasValue &&
                CheckCollisionBoxFrustum(obj.GlobalBoundingBox.Value, _frustum))
            {
                // Mark the object as processed
                processedObjects.Add(obj);

                // Render the object
                if (obj.IsEnabled)
                {
                    //DrawModelEx(_houseModel, obj.Position, Vector3.UnitY, 0, obj.Scale, Color.White);
                    DrawCubeV(obj.Position, new Vector3(11, 6.5f, 11), Color.RayWhite);
                }

                // Debug: Render bounding boxes
                if (obj.DrawBounds)
                {
                    DrawBoundingBox(obj.GlobalBoundingBox.Value, Color.Green);
                }

                _objectsRendered++;
            }
        }

        // Recursively traverse children
        if (!node.IsLeaf)
        {
            foreach (OctreeNode? child in node.Children)
            {
                TraverseAndRender(child, processedObjects);
            }
        }
    }

    private bool CheckCollisionBoxFrustum(BoundingBox box, Frustum frustum)
    {
        foreach (Vector4 plane in frustum.Planes)
        {
            // Calculate the positive vertex (furthest point in the direction of the plane normal)
            Vector3 positiveVertex = new(
                plane.X > 0 ? box.Max.X : box.Min.X,
                plane.Y > 0 ? box.Max.Y : box.Min.Y,
                plane.Z > 0 ? box.Max.Z : box.Min.Z
            );

            // Test if the positive vertex is outside the plane
            if (Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), positiveVertex) + plane.W < 0)
            {
                // Outside the frustum
                return false;
            }
        }

        // Inside or intersecting the frustum
        return true;
    }

    private void TraverseAndRender(SceneNode? node)
    {
        if (node == null)
        {
            return;
        }

        // Perform frustum culling using the global bounding box
        if (node.GlobalBoundingBox.HasValue && !CheckCollisionBoxFrustum(node.GlobalBoundingBox.Value, _frustum))
        {
            return; // Skip this node if it's outside the frustum
        }

        // Render individual mesh bounding boxes
        foreach (BoundingBox meshBoundingBox in node.MeshBoundingBoxes)
        {
            // DrawBoundingBox(meshBoundingBox, Color.Green);
        }

        // Render global bounding box
        if (node.GlobalBoundingBox.HasValue)
        {
            DrawBoundingBox(node.GlobalBoundingBox.Value, Color.Red);
        }

        // Apply the node's global transform
        Matrix4x4 transform = node.GlobalTransform;

        // If this node represents an object (like a model), render it
        if (node.Name == "House")
        {
            DrawModelEx(_houseModel, node.Position, Vector3.UnitY, 0, node.Scale, Color.White);
            _objectsRendered++;
        }

        // Traverse child nodes
        foreach (SceneNode? child in node.Children)
        {
            TraverseAndRender(child);
        }
    }

    private unsafe void DrawModelInstanced(Model model, List<Matrix4x4> transforms)
    {
        for (int i = 0; i < model.MeshCount; i++)
        {
            Mesh mesh = model.Meshes[i];
            Material material = model.Materials[model.MeshMaterial[i]];

            // Draw all instances of this mesh
            DrawMeshInstanced(mesh, material, transforms.ToArray(), transforms.Count);
        }
    }
}
