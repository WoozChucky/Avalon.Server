// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Utils;

public static class BlittableHelper<T>
{
    public static readonly bool IsBlittable;

    static BlittableHelper()
    {
        try
        {
            // Class test
            if (default(T) != null)
            {
                // Non-blittable types cannot allocate pinned handle
                GCHandle.Alloc(default(T), GCHandleType.Pinned).Free();
                IsBlittable = true;
            }
        }
        catch
        {
        }
    }
}
