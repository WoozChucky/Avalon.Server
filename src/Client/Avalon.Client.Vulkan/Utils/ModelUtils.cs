// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Engine;
using Avalon.Client.Vulkan.Systems.Simple;

namespace Avalon.Client.Vulkan.Utils;

public static class ModelUtils
{
    public static string LoadEmbeddedResource(string path, Type type)
    {
        using (Stream? s = type.Assembly.GetManifestResourceStream(path))
        {
            if (s is null)
            {
                return string.Empty;
            }

            using (StreamReader? sr = new(s))
            {
                return sr.ReadToEnd();
            }
        }
    }

    public static string GetEmbeddedResourceObjText(string filename)
    {
        Assembly? assembly = Assembly.GetExecutingAssembly();
        //foreach (var item in assembly.GetManifestResourceNames())
        //{
        //    Console.WriteLine($"{item}");
        //}
        //var resourceName = $"Chapter05SwapChain.{filename.Replace('/', '.')}";
        string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(s => s.EndsWith(filename));
        if (resourceName is null)
        {
            throw new ApplicationException(
                $"*** No obj file found with name {filename}\n*** Check that resourceName and try again!  Did you forget to set obj file to Embedded Resource/Do Not Copy?");
        }

        using (Stream? stream = assembly.GetManifestResourceStream(resourceName) ?? throw new ApplicationException(
                   $"*** No shader file found at {resourceName}\n*** Check that resourceName and try again!  Did you forget to set glsl file to Embedded Resource/Do Not Copy?"))
        {
            using (StreamReader? reader = new(stream))
            {
                string result = reader.ReadToEnd();
                return result;
            }
        }
    }


    public static LveModel LoadModelFromFile(Vk vk, LveDevice device, string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Error loading model file, Can't find file at {path}");
        }

        LveModel.Builder builder = new();
        builder.LoadModel(path);

        return new LveModel(vk, device, builder);
    }

    public static LveModel CreateCubeModel6(Vk vk, LveDevice device)
    {
        float h = .5f;
        LveModel.Builder builder = new()
        {
            Vertices = new Vertex[]
            {
                // left face (white)
                new(new Vector3(-h, -h, -h), Color3.White), new(new Vector3(-h, h, h), Color3.White),
                new(new Vector3(-h, -h, h), Color3.White), new(new Vector3(-h, h, -h), Color3.White),

                // x+ right face (red)
                new(new Vector3(h, -h, -h), Color3.Red), new(new Vector3(h, h, h), Color3.Red),
                new(new Vector3(h, -h, h), Color3.Red), new(new Vector3(h, h, -h), Color3.Red),

                // y+ top face (green, remember y axis points down)
                new(new Vector3(-h, -h, -h), Color3.Green), new(new Vector3(h, -h, h), Color3.Green),
                new(new Vector3(-h, -h, h), Color3.Green), new(new Vector3(h, -h, -h), Color3.Green),

                // bottom face (cyan)
                new(new Vector3(-h, h, -h), Color3.Cyan), new(new Vector3(h, h, h), Color3.Cyan),
                new(new Vector3(-h, h, h), Color3.Cyan), new(new Vector3(h, h, -h), Color3.Cyan),

                // z+ nose face (blue)
                new(new Vector3(-h, -h, h), Color3.Blue), new(new Vector3(h, h, h), Color3.Blue),
                new(new Vector3(-h, h, h), Color3.Blue), new(new Vector3(h, -h, h), Color3.Blue),

                // tail face (orange)
                new(new Vector3(-h, -h, -h), Color3.Orange), new(new Vector3(h, h, -h), Color3.Orange),
                new(new Vector3(-h, h, -h), Color3.Orange), new(new Vector3(h, -h, -h), Color3.Orange)
            },
            Indices = new uint[]
            {
                0, 1, 2, 0, 3, 1, 4, 5, 6, 4, 7, 5, 8, 9, 10, 8, 11, 9, 12, 13, 14, 12, 15, 13, 16, 17, 18, 16, 19,
                17, 20, 21, 22, 20, 23, 21
            }
        };

        return new LveModel(vk, device, builder);
    }


    public static LveModel CreateCubeModel3(Vk vk, LveDevice device)
    {
        float h = .5f;
        LveModel.Builder builder = new()
        {
            Vertices = new Vertex[]
            {
                // x+ right face (red)
                new(new Vector3(h, -h, -h), Color3.Red), new(new Vector3(h, h, h), Color3.Red),
                new(new Vector3(h, -h, h), Color3.Red), new(new Vector3(h, h, -h), Color3.Red),

                // y+ top face (green, remember y axis points down)
                new(new Vector3(-h, h, -h), Color3.Green), new(new Vector3(h, h, h), Color3.Green),
                new(new Vector3(-h, h, h), Color3.Green), new(new Vector3(h, h, -h), Color3.Green),

                // z+ nose face (blue)
                new(new Vector3(-h, -h, h), Color3.Blue), new(new Vector3(h, h, h), Color3.Blue),
                new(new Vector3(-h, h, h), Color3.Blue), new(new Vector3(h, -h, h), Color3.Blue)
            },
            Indices = new uint[] {0, 1, 2, 0, 3, 1, 4, 5, 6, 4, 7, 5, 8, 9, 10, 8, 11, 9}
        };

        return new LveModel(vk, device, builder);
    }
}
