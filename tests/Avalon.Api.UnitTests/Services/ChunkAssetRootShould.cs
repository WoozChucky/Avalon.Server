using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// A chunk asset is served only from inside the configured asset root (#559). The path comes
/// from a ChunkTemplate's GeometryFile, so a bad world-database value must not reach a sibling
/// folder, climb with "..", or name an absolute path; every refusal answers exactly as a
/// missing asset does (null, which the controller turns into a bare 404).
/// </summary>
public sealed class ChunkAssetRootShould : IDisposable
{
    private readonly string _parent;
    private readonly string _root;
    private readonly string _sibling;
    private readonly IChunkTemplateRepository _chunks = Substitute.For<IChunkTemplateRepository>();

    public ChunkAssetRootShould()
    {
        _parent = Path.Combine(Path.GetTempPath(), "avalon-559-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_parent, "assets");
        _sibling = Path.Combine(_parent, "assets-x");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        Directory.CreateDirectory(_sibling);
        File.WriteAllText(Path.Combine(_root, "inside.obj"), "v 0 0 0");
        File.WriteAllText(Path.Combine(_root, "sub", "nested.obj"), "v 1 1 1");
        File.WriteAllText(Path.Combine(_sibling, "outside.obj"), "v 9 9 9");
    }

    public void Dispose()
    {
        try { Directory.Delete(_parent, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private MapService Service(string root)
    {
        var options = Substitute.For<IOptionsSnapshot<MapAssetConfig>>();
        options.Value.Returns(new MapAssetConfig { ChunkAssetRoot = root });
        return new MapService(
            Substitute.For<IMapTemplateRepository>(),
            Substitute.For<IProceduralMapConfigRepository>(),
            Substitute.For<IProceduralLayoutInputsResolver>(),
            _chunks,
            Substitute.For<ILoggerFactory>(),
            options);
    }

    private void Template(string geometryFile) =>
        _chunks.FindAllWithSlotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ChunkTemplate> { new() { Id = 1, Name = "t", GeometryFile = geometryFile } });

    [Fact]
    public async Task Serve_a_file_inside_the_root()
    {
        Template("inside.obj");

        var asset = await Service(_root).GetChunkAssetAsync("inside.obj");

        Assert.NotNull(asset);
        Assert.Equal("v 0 0 0", System.Text.Encoding.UTF8.GetString(asset.Bytes));
    }

    [Fact]
    public async Task Serve_a_file_in_a_subfolder_of_the_root()
    {
        Template("sub/nested.obj");

        Assert.NotNull(await Service(_root).GetChunkAssetAsync("sub/nested.obj"));
    }

    [Fact]
    public async Task Serve_a_file_when_the_root_ends_in_a_separator()
    {
        Template("inside.obj");

        Assert.NotNull(await Service(_root + Path.DirectorySeparatorChar).GetChunkAssetAsync("inside.obj"));
    }

    [Fact]
    public async Task Refuse_a_geometry_file_that_resolves_into_a_sibling_folder()
    {
        Template("../assets-x/outside.obj");

        Assert.Null(await Service(_root).GetChunkAssetAsync("outside.obj"));
    }

    [Fact]
    public async Task Refuse_a_geometry_file_with_dot_dot_even_when_it_stays_inside()
    {
        Template("sub/../inside.obj");

        Assert.Null(await Service(_root).GetChunkAssetAsync("inside.obj"));
    }

    [Fact]
    public async Task Refuse_an_absolute_geometry_file_outside_the_root()
    {
        Template(Path.Combine(_sibling, "outside.obj"));

        Assert.Null(await Service(_root).GetChunkAssetAsync("outside.obj"));
    }

    [Fact]
    public async Task Refuse_an_absolute_geometry_file_even_inside_the_root()
    {
        Template(Path.Combine(_root, "inside.obj"));

        Assert.Null(await Service(_root).GetChunkAssetAsync("inside.obj"));
    }

    [Fact]
    public async Task Answer_a_refusal_exactly_as_a_missing_asset()
    {
        Template("missing.obj");
        var missing = await Service(_root).GetChunkAssetAsync("missing.obj");

        Template("../assets-x/outside.obj");
        var refused = await Service(_root).GetChunkAssetAsync("outside.obj");

        Assert.Null(missing);
        Assert.Equal(missing, refused);
    }
}
