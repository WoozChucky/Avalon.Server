using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Database.World.Repositories;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Services;

public interface IMapService
{
    Task<PagedResult<MapTemplateDto>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<MapTemplateDto?> GetAsync(ushort id, CancellationToken ct = default);
    Task<LayoutPreviewDto?> PreviewLayoutAsync(ushort id, int? seed, CancellationToken ct = default);
    Task<ChunkAssetResult?> GetChunkAssetAsync(string filename, CancellationToken ct = default);
}

public sealed record ChunkAssetResult(byte[] Bytes, string ContentType);

public class MapService : IMapService
{
    private readonly IMapTemplateRepository _mapRepo;
    private readonly IProceduralMapConfigRepository _configRepo;
    private readonly IProceduralLayoutInputsResolver _inputsResolver;
    private readonly IChunkTemplateRepository _chunkRepo;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsSnapshot<MapAssetConfig> _assetConfig;

    public MapService(
        IMapTemplateRepository mapRepo,
        IProceduralMapConfigRepository configRepo,
        IProceduralLayoutInputsResolver inputsResolver,
        IChunkTemplateRepository chunkRepo,
        ILoggerFactory loggerFactory,
        IOptionsSnapshot<MapAssetConfig> assetConfig)
    {
        _mapRepo = mapRepo;
        _configRepo = configRepo;
        _inputsResolver = inputsResolver;
        _chunkRepo = chunkRepo;
        _loggerFactory = loggerFactory;
        _assetConfig = assetConfig;
    }

    public async Task<PagedResult<MapTemplateDto>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var filter = new MapTemplatePaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
        };
        var result = await _mapRepo.PaginateAsync(filter, track: false, ct);
        return result.MapTo(t => t.ToDto());
    }

    public async Task<MapTemplateDto?> GetAsync(ushort id, CancellationToken ct = default)
    {
        var template = await _mapRepo.FindByIdAsync(new MapTemplateId(id), track: false, ct);
        return template?.ToDto();
    }

    public async Task<LayoutPreviewDto?> PreviewLayoutAsync(ushort id, int? seed, CancellationToken ct = default)
    {
        var template = await _mapRepo.FindByIdAsync(new MapTemplateId(id), track: false, ct);
        if (template is null) return null;

        var config = await _configRepo.FindByTemplateIdAsync(template.Id, ct)
            ?? throw new BusinessException($"Map {id} has no ProceduralMapConfig — preview only works for procedural maps.");

        var pool = await _inputsResolver.FindPoolAsync(config.ChunkPoolId, ct)
            ?? throw new BusinessException($"Pool {config.ChunkPoolId.Value} not found.");
        if (pool.Memberships.Count == 0)
            throw new BusinessException($"Pool {config.ChunkPoolId.Value} has no memberships.");

        var resolution = await _inputsResolver.ResolveMembersAsync(pool, ct);

        var effectiveSeed = seed ?? Random.Shared.Next();
        var generator = new ProceduralLayoutGenerator(_loggerFactory);

        try
        {
            var layout = generator.Generate(config, resolution.Members, effectiveSeed);
            return layout.ToDto(resolution.TemplatesById);
        }
        catch (ProceduralGenerationFailedException ex)
        {
            throw new BusinessException(ex.Message);
        }
        catch (InvalidProceduralConfigException ex)
        {
            throw new BusinessException(ex.Message);
        }
    }

    /// <summary>
    /// Loads a single chunk geometry .obj by filename. The filename MUST match a
    /// <see cref="Avalon.Domain.World.ChunkTemplate.GeometryFile"/> row to block path
    /// traversal — we never trust the route segment directly. Returns null when the
    /// file is unknown, missing on disk, or not confined to the asset root (#559); all three
    /// answer alike.
    /// </summary>
    public async Task<ChunkAssetResult?> GetChunkAssetAsync(string filename, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filename)) return null;

        var allTemplates = await _chunkRepo.FindAllWithSlotsAsync(ct);
        var match = allTemplates.FirstOrDefault(t =>
            string.Equals(t.GeometryFile, filename, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(System.IO.Path.GetFileName(t.GeometryFile), filename, StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;

        var root = _assetConfig.Value.ChunkAssetRoot;
        if (string.IsNullOrWhiteSpace(root)) return null;

        var fullPath = ResolveInsideRoot(root, match.GeometryFile);
        if (fullPath is null) return null;
        if (!System.IO.File.Exists(fullPath)) return null;

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
        return new ChunkAssetResult(bytes, "model/obj");
    }

    /// <summary>
    /// The full path of <paramref name="geometryFile"/> under <paramref name="root"/>, or null when
    /// it is not confined to the root (#559). A GeometryFile must be relative and never contain a
    /// ".." segment, and the resolved path must lie under the root followed by a separator, so a
    /// sibling folder such as "assets-x" never counts as inside "assets". Every refusal is the
    /// caller's plain "not found", and nothing about the path is logged or returned.
    /// </summary>
    private static string? ResolveInsideRoot(string root, string geometryFile)
    {
        if (System.IO.Path.IsPathRooted(geometryFile)) return null;
        if (geometryFile.Split('/', '\\').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal))) return null;

        var rootFull = System.IO.Path.GetFullPath(root);
        if (!System.IO.Path.EndsInDirectorySeparator(rootFull))
            rootFull += System.IO.Path.DirectorySeparatorChar;

        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, geometryFile));

        // Compare as the file system does: case-sensitive on Linux, case-insensitive on
        // Windows and macOS (their default volumes).
        var comparison = System.OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return fullPath.StartsWith(rootFull, comparison) ? fullPath : null;
    }
}
