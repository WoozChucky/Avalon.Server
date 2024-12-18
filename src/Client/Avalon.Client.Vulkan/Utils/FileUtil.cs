// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Utils;

public sealed class FileUtil
{
    public static byte[] GetShaderBytes(string filename, string renderSystemName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (string item in assembly.GetManifestResourceNames())
        {
            Console.Write(".");
            Console.WriteLine($"{item}");
        }

        string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(s => s.EndsWith(filename));
        if (resourceName is null)
        {
            throw new ApplicationException(
                $"*** In {renderSystemName}, No shader file found with name {filename}\n*** Check that resourceName and try again!  Did you forget to set glsl file to Embedded Resource/Do Not Copy?");
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName) ?? throw new ApplicationException(
            $"*** In {renderSystemName}, No shader file found at {resourceName}\n*** Check that resourceName and try again!  Did you forget to set glsl file to Embedded Resource/Do Not Copy?");
        using MemoryStream ms = new();

        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
