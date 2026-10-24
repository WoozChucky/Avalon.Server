using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Avalon.Api.Contract.Crashes;

[JsonConverter(typeof(UpdateCrashGroupRequestConverter))]
public sealed class UpdateCrashGroupRequest
{
    private string? _status;
    private string? _fixedInVersion;
    private string? _issueUrl;
    private string? _notes;

    [Required]
    [Range(0, long.MaxValue)]
    public required long TriageRevision { get; init; }
    public string? Status
    {
        get => _status;
        set
        {
            HasStatus = true;
            _status = value;
        }
    }
    public string? FixedInVersion
    {
        get => _fixedInVersion;
        set
        {
            HasFixedInVersion = true;
            _fixedInVersion = value;
        }
    }
    public string? IssueUrl
    {
        get => _issueUrl;
        set
        {
            HasIssueUrl = true;
            _issueUrl = value;
        }
    }
    /// <summary>Plain engineering notes only; never include logs, names, paths or report material in these long-lived notes.</summary>
    [MaxLength(4096)]
    public string? Notes
    {
        get => _notes;
        set
        {
            HasNotes = true;
            _notes = value;
        }
    }

    // Presence is transport parsing state, never part of the public schema.
    [JsonIgnore]
    public bool HasStatus { get; private set; }
    [JsonIgnore]
    public bool HasFixedInVersion { get; private set; }
    [JsonIgnore]
    public bool HasIssueUrl { get; private set; }
    [JsonIgnore]
    public bool HasNotes { get; private set; }
}

public sealed class UpdateCrashGroupRequestConverter : JsonConverter<UpdateCrashGroupRequest>
{
    public override UpdateCrashGroupRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("triageRevision", out JsonElement revision)
            || revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out long value) || value < 0)
        {
            throw new JsonException("Invalid crash patch.");
        }
        var result = new UpdateCrashGroupRequest { TriageRevision = value };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new JsonException("Invalid crash patch.");
            if (property.NameEquals("triageRevision")) continue;
            if (property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                throw new JsonException("Invalid crash patch.");
            string? text = property.Value.GetString();
            switch (property.Name)
            {
                case "status":
                    if (text is not ("new" or "investigating" or "fixed" or "ignored")) throw new JsonException("Invalid crash patch.");
                    result.Status = text;
                    break;
                case "fixedInVersion":
                    result.FixedInVersion = text;
                    break;
                case "issueUrl":
                    result.IssueUrl = text;
                    break;
                case "notes":
                    result.Notes = text;
                    break;
                default: throw new JsonException("Invalid crash patch.");
            }
        }
        if (seen.Count < 2) throw new JsonException("Invalid crash patch.");
        return result;
    }

    public override void Write(Utf8JsonWriter writer, UpdateCrashGroupRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("triageRevision", value.TriageRevision);
        if (value.HasStatus) writer.WriteString("status", value.Status);
        if (value.HasFixedInVersion) writer.WriteString("fixedInVersion", value.FixedInVersion);
        if (value.HasIssueUrl) writer.WriteString("issueUrl", value.IssueUrl);
        if (value.HasNotes) writer.WriteString("notes", value.Notes);
        writer.WriteEndObject();
    }
}
