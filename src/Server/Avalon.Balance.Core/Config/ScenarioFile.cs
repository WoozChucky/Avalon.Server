using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

public sealed class PackEntry
{
    public CreatureRarity? Rarity { get; set; }

    public ulong? Template { get; set; }

    public int Count { get; set; } = 1;
}

public sealed class Scenario
{
    public string Id { get; set; } = "";

    public PackEntry[] Pack { get; set; } = [];

    /// <summary>A number of levels above the player, or "template" to roll the template's own range. Absent is 0.</summary>
    public JsonElement? LevelOffset { get; set; }

    /// <summary>At most this many creatures a cone hits; null hits every living one.</summary>
    public int? ConeHits { get; set; }

    [JsonIgnore]
    public int? Offset => LevelOffset switch
    {
        null => 0,
        { ValueKind: JsonValueKind.Number } e => e.TryGetInt32(out int offset) ? offset : null,
        _ => null,
    };

    [JsonIgnore]
    public bool OffsetFromTemplate =>
        LevelOffset is { ValueKind: JsonValueKind.String } e && string.Equals(e.GetString(), "template", StringComparison.Ordinal);

    /// <summary>Creatures at the player's own level: the scenarios the flat-curve check reads.</summary>
    [JsonIgnore]
    public bool SameLevel => Offset == 0;
}

public sealed class ScenarioFile
{
    public const string NoGear = "none";

    public int Runs { get; set; } = 1000;

    public int Seed { get; set; }

    /// <summary>An inclusive range, [first, last].</summary>
    public ushort[] Levels { get; set; } = [1, 1];

    public CharacterClass[] Classes { get; set; } = [];

    public string[] Gear { get; set; } = [NoGear];

    public Scenario[] Scenarios { get; set; } = [];

    public Dictionary<string, Dictionary<CharacterClass, ulong[]>> GearProfiles { get; set; } = new(StringComparer.Ordinal);

    public IReadOnlyList<ushort> LevelRange() =>
        Enumerable.Range(Levels[0], Levels[1] - Levels[0] + 1).Select(l => (ushort)l).ToList();

    public ulong[] GearFor(string gear, CharacterClass characterClass) =>
        string.Equals(gear, NoGear, StringComparison.Ordinal) ? [] : GearProfiles[gear][characterClass];

    public Scenario Find(string id) =>
        Scenarios.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Unknown scenario '{id}' (known: {string.Join(", ", Scenarios.Select(s => s.Id))})");

    /// <exception cref="InvalidDataException">The first problem, naming the scenario, gear or setting.</exception>
    public void Validate(BalanceData data)
    {
        if (Runs < 1) throw new InvalidDataException($"scenarios: runs must be 1 or more, not {Runs}");
        if (Levels is not [var first, var last] || first < 1 || first > last)
            throw new InvalidDataException("scenarios: levels must be [first, last] with 1 <= first <= last");
        if (Classes is null) throw new InvalidDataException("scenarios: classes is missing");
        if (Classes.Length == 0) throw new InvalidDataException("scenarios: classes is empty");
        if (Gear is null || Gear.Any(g => g is null)) throw new InvalidDataException("scenarios: gear is missing or names a null profile");
        if (GearProfiles is null) throw new InvalidDataException("scenarios: gearProfiles is missing");
        if (Scenarios is null || Scenarios.Any(s => s is null)) throw new InvalidDataException("scenarios: scenarios is missing or holds a null scenario");

        foreach (string gear in Gear)
        {
            if (string.Equals(gear, NoGear, StringComparison.Ordinal)) continue;
            if (!GearProfiles.TryGetValue(gear, out Dictionary<CharacterClass, ulong[]>? profile) || profile is null)
                throw new InvalidDataException($"scenarios: gear '{gear}' has no profile in gearProfiles");
            foreach (CharacterClass c in Classes)
            {
                if (!profile.TryGetValue(c, out ulong[]? ids) || ids is null)
                    throw new InvalidDataException($"scenarios: gear '{gear}' names no items for {c}");
                foreach (ulong id in ids) data.Item(id);
            }
        }

        if (Scenarios.Any(s => s.Id is null)) throw new InvalidDataException("scenarios: a scenario has no id");
        if (Scenarios.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != Scenarios.Length)
            throw new InvalidDataException("scenarios: two scenarios share an id");

        foreach (Scenario s in Scenarios)
        {
            if (s.Pack is null) throw new InvalidDataException($"scenario '{s.Id}': the pack is missing");
            if (s.Pack.Any(e => e is null)) throw new InvalidDataException($"scenario '{s.Id}': the pack holds a null entry");
            if (s.Pack.Length == 0) throw new InvalidDataException($"scenario '{s.Id}': the pack is empty");
            if (s.Offset is null && !s.OffsetFromTemplate)
                throw new InvalidDataException($"scenario '{s.Id}': levelOffset must be a number or \"template\"");
            if (s.ConeHits is < 1) throw new InvalidDataException($"scenario '{s.Id}': coneHits must be 1 or more");

            foreach (PackEntry entry in s.Pack)
            {
                if (entry.Count < 1) throw new InvalidDataException($"scenario '{s.Id}': a pack count must be 1 or more");
                if ((entry.Rarity is null) == (entry.Template is null))
                    throw new InvalidDataException($"scenario '{s.Id}': a pack entry names a rarity or a template, exactly one");
                if (entry.Template is { } id && !data.HostileTemplates.Any(t => t.Id.Value == id))
                    throw new InvalidDataException($"scenario '{s.Id}': template {id} is not a hostile creature with a kit");
                if (entry.Rarity is { } rarity && data.HostileOfRarity(rarity).Count == 0)
                    throw new InvalidDataException($"scenario '{s.Id}': no hostile {rarity} template to draw");
            }
        }
    }
}
