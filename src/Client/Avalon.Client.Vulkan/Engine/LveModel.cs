// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Systems.Simple;
using Avalon.Client.Vulkan.Utils;
using JeremyAnsel.Media.WavefrontObj;

namespace Avalon.Client.Vulkan.Engine;

public class LveModel : IDisposable
{
    private readonly LveDevice device = null!;

    private readonly bool hasIndexBuffer;
    private readonly uint indexCount;
    private readonly uint vertexCount;
    private readonly Vk vk = null!;
    private LveBuffer indexBuffer = null!;

    private LveBuffer vertexBuffer = null!;

    public LveModel(Vk vk, LveDevice device, Builder builder)
    {
        this.vk = vk;
        this.device = device;
        vertexCount = (uint)builder.Vertices.Length;
        createVertexBuffers(builder.Vertices);
        indexCount = (uint)builder.Indices.Length;
        if (indexCount > 0)
        {
            hasIndexBuffer = true;
            createIndexBuffers(builder.Indices);
        }
    }

    public void Dispose()
    {
        vertexBuffer.Dispose();
        indexBuffer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void createVertexBuffers(Vertex[] vertices)
    {
        ulong instanceSize = Vertex.SizeOf();
        ulong bufferSize = instanceSize * (ulong)vertices.Length;

        LveBuffer stagingBuffer = new(vk, device,
            instanceSize, vertexCount,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
        );
        stagingBuffer.Map();
        stagingBuffer.WriteToBuffer(vertices);

        vertexBuffer = new LveBuffer(vk, device,
            instanceSize, vertexCount,
            BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.DeviceLocalBit
        );

        device.CopyBuffer(stagingBuffer.VkBuffer, vertexBuffer.VkBuffer, bufferSize);
    }

    private void createIndexBuffers(uint[] indices)
    {
        ulong instanceSize = (ulong)Unsafe.SizeOf<uint>();
        ulong bufferSize = instanceSize * (ulong)indices.Length;

        LveBuffer stagingBuffer = new(vk, device,
            instanceSize, indexCount,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
        );
        stagingBuffer.Map();
        stagingBuffer.WriteToBuffer(indices);

        indexBuffer = new LveBuffer(vk, device,
            instanceSize, indexCount,
            BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.DeviceLocalBit
        );

        device.CopyBuffer(stagingBuffer.VkBuffer, indexBuffer.VkBuffer, bufferSize);
    }

    public unsafe void Bind(CommandBuffer commandBuffer)
    {
        Buffer[] vertexBuffers = new[] {vertexBuffer.VkBuffer};
        ulong[] offsets = new ulong[] {0};

        fixed (ulong* offsetsPtr = offsets)
        fixed (Buffer* vertexBuffersPtr = vertexBuffers)
        {
            vk.CmdBindVertexBuffers(commandBuffer, 0, 1, vertexBuffersPtr, offsetsPtr);
        }

        if (hasIndexBuffer)
        {
            vk.CmdBindIndexBuffer(commandBuffer, indexBuffer.VkBuffer, 0, IndexType.Uint32);
        }
    }

    public void Draw(CommandBuffer commandBuffer)
    {
        if (hasIndexBuffer)
        {
            vk.CmdDrawIndexed(commandBuffer, indexCount, 1, 0, 0, 0);
        }
        else
        {
            vk.CmdDraw(commandBuffer, vertexCount, 1, 0, 0);
        }
    }


    public struct Builder
    {
        public Vertex[] Vertices;
        public uint[] Indices;

        public Builder()
        {
            Vertices = Array.Empty<Vertex>();
            Indices = Array.Empty<uint>();
        }

        public void LoadModel(string path)
        {
            log.d("obj", $" loading {path}...");

            ObjFile objFile = ObjFile.FromFile(path);

            Dictionary<Vertex, uint> vertexMap = new();
            List<Vertex> vertices = new();
            List<uint> indices = new();

            foreach (ObjFace face in objFile.Faces)
            {
                foreach (ObjTriplet vFace in face.Vertices)
                {
                    int vertexIndex = vFace.Vertex;
                    ObjVertex vertex = objFile.Vertices[vertexIndex - 1];
                    Vector3 positionOut = new(vertex.Position.X, -vertex.Position.Y, vertex.Position.Z);
                    Vector3 colorOut = Vector3.Zero;
                    if (vertex.Color is not null)
                    {
                        colorOut = new Vector3(vertex.Color.Value.X, vertex.Color.Value.Y, vertex.Color.Value.Z);
                    }
                    else
                    {
                        colorOut = new Vector3(1f, 1f, 1f);
                    }

                    int normalIndex = vFace.Normal;
                    ObjVector3 normal = objFile.VertexNormals[normalIndex - 1];
                    Vector3 normalOut = new(normal.X, -normal.Y, normal.Z);

                    int textureIndex = vFace.Texture;
                    ObjVector3 texture = objFile.TextureVertices[textureIndex - 1];
                    //Flip Y for OBJ in Vulkan
                    Vector2 textureOut = new(texture.X, -texture.Y);

                    Vertex vertexOut = new()
                    {
                        Position = positionOut, Color = colorOut, Normal = normalOut, UV = textureOut
                    };
                    if (vertexMap.TryGetValue(vertexOut, out uint meshIndex))
                    {
                        indices.Add(meshIndex);
                    }
                    else
                    {
                        indices.Add((uint)vertices.Count);
                        vertexMap[vertexOut] = (uint)vertices.Count;
                        vertices.Add(vertexOut);
                    }
                }
            }

            Vertices = vertices.ToArray();
            Indices = indices.ToArray();

            log.d("obj", $" done {Vertices.Length} verts, {Indices.Length} indices");
        }
    }
}
