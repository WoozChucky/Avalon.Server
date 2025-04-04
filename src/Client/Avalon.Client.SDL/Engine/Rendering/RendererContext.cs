// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.SDL.Engine.Rendering.Assets;
using Avalon.Client.SDL.Engine.Rendering.Passes;

namespace Avalon.Client.SDL.Engine.Rendering;

internal unsafe struct RendererContext
{
    public SDL_GPUCommandBuffer* CommandBuffer { get; set; }
    public SDL_GPUTexture* SwapchainTexture { get; set; }
    public MeshAsset FullScreenQuad { get; set; } // TODO: Remove from this layer
    public RenderStage Stage;
    public ShadowPassContext Shadow;
    public GeometryPassContext Geometry;
    public AmbientOcclusionContextPass SSAO;
    public LightningContextPass Lightning;
    public PostProcessPassContext PostProcess;
    public PresentPassContext Present;
}
