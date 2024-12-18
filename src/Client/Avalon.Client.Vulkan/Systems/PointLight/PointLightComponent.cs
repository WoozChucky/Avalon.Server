// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Systems.PointLight;

public struct PointLightComponent
{
    public float LightIntensity;

    public PointLightComponent(float intensity) => LightIntensity = intensity;
}
