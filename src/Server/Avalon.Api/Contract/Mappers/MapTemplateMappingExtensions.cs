using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.Api.Contract.Mappers;

public static class MapTemplateMappingExtensions
{
    public static MapTemplateDto ToDto(this MapTemplate t) => new()
    {
        Id = t.Id.Value,
        Name = t.Name,
        Description = t.Description,
        MapType = (Avalon.Api.Contract.MapType)t.MapType,
        PvP = t.PvP,
        MinLevel = t.MinLevel,
        MaxLevel = t.MaxLevel,
        AreaTableId = t.AreaTableId,
        LoadingScreenId = t.LoadingScreenId,
        CorpseX = t.CorpseX,
        CorpseY = t.CorpseY,
        CorpseZ = t.CorpseZ,
        MaxPlayers = t.MaxPlayers,
        DefaultSpawnX = t.DefaultSpawnX,
        DefaultSpawnY = t.DefaultSpawnY,
        DefaultSpawnZ = t.DefaultSpawnZ,
        LogoutMapId = t.LogoutMapId,
    };

    public static LayoutPreviewDto ToDto(
        this ChunkLayout layout,
        IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> byId)
    {
        var chunks = layout.Chunks.Select(c => c.ToDto(byId)).ToList();
        return new LayoutPreviewDto
        {
            Seed = layout.Seed,
            MapTemplateId = layout.Config?.MapTemplateId.Value ?? 0,
            CellSize = layout.CellSize,
            EntrySpawnX = layout.EntrySpawnWorldPos.x,
            EntrySpawnY = layout.EntrySpawnWorldPos.y,
            EntrySpawnZ = layout.EntrySpawnWorldPos.z,
            EntryChunk = layout.EntryChunk?.ToDto(byId),
            BossChunk = layout.BossChunk?.ToDto(byId),
            Chunks = chunks,
            Portals = layout.Portals.Select(p => p.ToDto()).ToList(),
        };
    }

    public static ChunkPreviewDto ToDto(
        this PlacedChunk c,
        IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> byId)
    {
        var hasTemplate = byId.TryGetValue(c.TemplateId, out var t);
        return new ChunkPreviewDto
        {
            TemplateId = c.TemplateId.Value,
            TemplateName = hasTemplate ? t!.Name : "",
            GeometryFile = hasTemplate ? t!.GeometryFile : "",
            GridX = c.GridX,
            GridZ = c.GridZ,
            Rotation = c.Rotation,
            WorldX = c.WorldPos.x,
            WorldY = c.WorldPos.y,
            WorldZ = c.WorldPos.z,
        };
    }

    public static PortalPreviewDto ToDto(this PortalPlacement p) => new()
    {
        Role = p.Role.ToString(),
        TargetMapId = p.TargetMapId,
        WorldX = p.WorldPos.x,
        WorldY = p.WorldPos.y,
        WorldZ = p.WorldPos.z,
        Radius = p.Radius,
    };
}
