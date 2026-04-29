namespace Avalon.Api.Config;

/// <summary>
/// Where on disk the chunk geometry .obj files live, used by the admin layout-preview
/// endpoint to serve assets to the SPA. In dev points at the World server's
/// <c>Maps/</c> directory; in production should be a published artifact path.
/// </summary>
public class MapAssetConfig
{
    public string ChunkAssetRoot { get; set; } = "";
}
