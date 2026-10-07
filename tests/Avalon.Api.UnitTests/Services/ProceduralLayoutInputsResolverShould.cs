// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public class ProceduralLayoutInputsResolverShould
{
    private readonly IChunkPoolRepository _pools = Substitute.For<IChunkPoolRepository>();
    private readonly IChunkTemplateRepository _chunks = Substitute.For<IChunkTemplateRepository>();

    private static readonly ChunkTemplate s_entry = new() { Id = new ChunkTemplateId(1), Name = "entry" };
    private static readonly ChunkTemplate s_west = new() { Id = new ChunkTemplateId(2), Name = "west" };
    private static readonly ChunkTemplate s_east = new() { Id = new ChunkTemplateId(3), Name = "east" };

    private static ChunkPool PoolWithGroup(ChunkTemplateId eastId) => new()
    {
        Id = new ChunkPoolId(1),
        Name = "p1",
        Memberships = [new ChunkPoolMembership { ChunkPoolId = new ChunkPoolId(1), ChunkTemplateId = s_entry.Id, Weight = 1f }],
        Groups =
        [
            new ChunkGroup
            {
                Id = 1, Name = "arena", ChunkPoolId = new ChunkPoolId(1),
                Members =
                [
                    new ChunkGroupMember { ChunkGroupId = 1, ChunkTemplateId = s_west.Id, CellX = 0, CellZ = 0 },
                    new ChunkGroupMember { ChunkGroupId = 1, ChunkTemplateId = eastId, CellX = 1, CellZ = 0 },
                ]
            }
        ]
    };

    [Fact]
    public async Task Resolve_a_pools_set_pieces_alongside_its_members()
    {
        _chunks.FindAllWithSlotsAsync(Arg.Any<CancellationToken>()).Returns([s_entry, s_west, s_east]);

        ProceduralPoolResolution resolution = await new ProceduralLayoutInputsResolver(_pools, _chunks)
            .ResolveMembersAsync(PoolWithGroup(s_east.Id), CancellationToken.None);

        Assert.Equal("entry", Assert.Single(resolution.Members).Template.Name);
        ChunkGroupDefinition group = Assert.Single(resolution.Groups!);
        Assert.Equal("arena", group.Name);
        Assert.Equal(["west", "east"], group.Cells.OrderBy(c => c.CellX).Select(c => c.Template.Name));
    }

    [Fact]
    public async Task Leave_out_a_set_piece_whose_template_is_unknown()
    {
        _chunks.FindAllWithSlotsAsync(Arg.Any<CancellationToken>()).Returns([s_entry, s_west, s_east]);

        ProceduralPoolResolution resolution = await new ProceduralLayoutInputsResolver(_pools, _chunks)
            .ResolveMembersAsync(PoolWithGroup(new ChunkTemplateId(99)), CancellationToken.None);

        Assert.Empty(resolution.Groups!);
    }
}
