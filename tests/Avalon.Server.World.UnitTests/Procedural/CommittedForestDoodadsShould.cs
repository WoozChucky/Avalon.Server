using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// The shipped forest's doodads: the committed Maps catalog and every seeded doodad template, loaded into SQLite as the
/// World server loads them at start-up (<see cref="ScenarioReferenceData" />), planned on the layouts the World server
/// generates. A required doodad that finds no slot falls back to open navmesh; this proves the committed pool never
/// needs to, so every required doodad stands where its pieces were authored to hold one.
/// </summary>
public class CommittedForestDoodadsShould
{
    [Fact]
    public void Place_every_required_doodad_at_a_slot_for_every_seed()
    {
        ScenarioReferenceData reference = ScenarioReferenceData.Shared;
        ProceduralMapConfig config = reference.ForestConfig;
        DoodadCatalog catalog = reference.Data.Doodads;
        Assert.All(config.DoodadRules, rule => Assert.True(catalog.TryGet(rule.DoodadTemplateId, out _),
            $"doodad rule {rule.Sequence} names template {rule.DoodadTemplateId.Value}, which the catalog does not hold"));

        IReadOnlyList<ChunkPoolMember> pool = reference.Chunks.GetByPool(config.ChunkPoolId);
        IReadOnlyList<ChunkGroupDefinition> groups = reference.Chunks.GetGroupsByPool(config.ChunkPoolId);
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        var service = new DoodadPlacementService(
            new DoodadScripts(new ServiceCollection().BuildServiceProvider(),
                TimeProvider.System, NullLogger<DoodadScripts>.Instance),
            TimeProvider.System, NullLoggerFactory.Instance);

        for (int seed = 0; seed < 1000; seed++)
        {
            ChunkLayout layout = generator.Generate(config, pool, seed, groups);
            DoodadPlan plan = service.Plan(layout, config, catalog, reference.Chunks);
            Assert.True(plan.RequiredShort.Count == 0,
                $"seed {seed}: {plan.RequiredShort.Count} required doodads found no slot ({string.Join(", ", plan.RequiredShort.Select(s => s.Template.Name))})");
        }
    }
}
