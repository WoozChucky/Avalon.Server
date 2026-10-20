using System.Globalization;
using Avalon.Common.Accounts;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Items;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

/// <summary>
/// <c>/spawn &lt;creature&gt; [count] [radius] [idle]</c> (GameMaster): <c>count</c> creatures of one template spread
/// over a disc of <c>radius</c> metres around the caller, each gone after <see cref="Lifetime" />. <c>idle</c> spawns
/// them without their AI script, standing where they are placed; <c>/despawn</c> removes them. A testing tool: the
/// same scene on any map layout.
/// </summary>
public sealed class SpawnCommand(IWorld world, ICreaturePlacementService placement, GmSpawns spawns,
    ILogger<SpawnCommand> logger) : ICommand
{
    public const int MaxCount = 50;
    public const float MaxRadius = 40f;
    public const float DefaultRadius = 10f;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const string Usage = "Usage: /spawn <creature id or name> [count 1-50] [radius 1-40] [idle]";
    private const int MaxCandidates = 5;

    // Successive points turn by the golden angle (radians), so any count covers the disc without clumping.
    private const float GoldenAngle = 2.3999632f;

    public string Name => "spawn";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public void Execute(CommandContext ctx, string[] args)
    {
        if (!TryParse(args, out string creature, out int count, out float radius, out bool idle))
        {
            ctx.Reply(Usage);
            return;
        }

        CreatureTemplate? template = Find(world.Data.Creatures.Templates, creature, out IReadOnlyList<CreatureTemplate> candidates);
        if (template is null)
        {
            ctx.Reply(candidates.Count == 0
                ? $"No creature template matches '{creature}'."
                : $"'{creature}' matches several: {string.Join(", ", candidates.Take(MaxCandidates).Select(t => $"{t.Id.Value} {t.Name}"))}" +
                  (candidates.Count > MaxCandidates ? ", ..." : "."));
            return;
        }

        if (ctx.Connection.Character is not CharacterEntity character)
        {
            ctx.Reply("No character selected.");
            return;
        }

        if (world.InstanceRegistry.GetInstanceById(character.InstanceId) is not { } instance)
        {
            ctx.Reply("You are not in a map.");
            return;
        }

        spawns.ForgetGone(world.InstanceRegistry);
        int placed = 0;
        foreach (Vector3 point in Points(character.Position, count, radius))
        {
            if (placement.SpawnAt(instance, template.Id, point, withScript: !idle) is not { } spawned)
                continue;
            placed++;
            spawns.Add(character.InstanceId, spawned);
            if (instance is IItemUseHost host)
                host.DespawnAfter(spawned, Lifetime);
        }

        logger.LogInformation("Account {AccountId} spawned {Placed} of {Count} {Template} ({TemplateId}, idle {Idle}) around character {CharacterId}",
            ctx.Connection.AccountId, placed, count, template.Name, template.Id.Value, idle, character.Guid);

        int missed = count - placed;
        ctx.Reply(placed == 0
            ? $"No {template.Name} could be placed: no ground nearby."
            : $"Spawned {placed} {template.Name}{(idle ? " (idle)" : "")}{(missed > 0 ? $"; {missed} found no ground" : "")}.");
    }

    /// <summary>
    /// The template <paramref name="text" /> names: a number is an id; otherwise the one name equal to it ignoring
    /// case, else the one name containing it. Null when none or several match; <paramref name="candidates" /> holds
    /// the several, by id.
    /// </summary>
    public static CreatureTemplate? Find(IEnumerable<CreatureTemplate> templates, string text,
        out IReadOnlyList<CreatureTemplate> candidates)
    {
        candidates = [];
        if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
            return templates.FirstOrDefault(t => t.Id.Value == id);

        var matches = templates
            .Where(t => string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Id.Value)
            .ToList();
        if (matches.Count == 0)
        {
            matches = templates
                .Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Id.Value)
                .ToList();
        }
        if (matches.Count == 1)
            return matches[0];
        candidates = matches;
        return null;
    }

    /// <summary><paramref name="count" /> points spread evenly over the disc of <paramref name="radius" /> on X/Z.</summary>
    public static IReadOnlyList<Vector3> Points(Vector3 centre, int count, float radius)
    {
        var points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float distance = radius * MathF.Sqrt((i + 0.5f) / count);
            float angle = i * GoldenAngle;
            points[i] = new Vector3(centre.x + distance * MathF.Cos(angle), centre.y, centre.z + distance * MathF.Sin(angle));
        }

        return points;
    }

    // The creature is every word before the first number or `idle` (chat splits on spaces), or the id alone; then the
    // count, then the radius, read the same on any culture; `idle` anywhere after the creature.
    private static bool TryParse(string[] args, out string creature, out int count, out float radius, out bool idle)
    {
        creature = string.Empty;
        count = 1;
        radius = DefaultRadius;
        idle = false;
        if (args.Length == 0)
            return false;

        bool byId = IsNumber(args[0]);
        int words = 1;
        int numbers = 0;
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "idle", StringComparison.OrdinalIgnoreCase))
            {
                idle = true;
            }
            else if (!IsNumber(arg))
            {
                if (byId || numbers > 0 || idle || words != i)
                    return false;
                words++;
            }
            else if (numbers == 0)
            {
                if (!int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count is < 1 or > MaxCount)
                    return false;
                numbers++;
            }
            else if (numbers == 1)
            {
                // NaN parses as a number and fails no range test, so the range is asked of a finite value.
                if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                    !float.IsFinite(radius) || radius is < 1f or > MaxRadius)
                {
                    return false;
                }
                numbers++;
            }
            else
            {
                return false;
            }
        }

        creature = string.Join(' ', args, 0, words);
        return true;
    }

    // Anything that reads as a number (a sign, an exponent, NaN) is one, so it is checked as a count or a radius and
    // never taken for part of a name.
    private static bool IsNumber(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
