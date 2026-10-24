using System.Text.Json.Nodes;
using Avalon.Api.Contract.Crashes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Avalon.Api.Identity.Crashes;

/// <summary>Describes the strict converter's wire object and the finite crash transport values.</summary>
internal sealed class CrashOpenApiTransformer : IOpenApiSchemaTransformer, IOpenApiOperationTransformer
{
    private static readonly string[] s_groupStatuses = ["new", "investigating", "fixed", "ignored"];
    private static readonly string[] s_reportStatuses = ["pending", "processing", "retry_wait", "processed", "rejected", "failed"];
    private static readonly string[] s_quality = ["missingSystemSymbols", "scanFrames", "truncated", "metadataDisagreement", "versionComparisonUnavailable", "unknownNativeValue"];

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        Type type = context.JsonTypeInfo.Type;
        if (type == typeof(UpdateCrashGroupRequest))
        {
            // A custom JsonConverter has no inspectable JSON properties. Keep presence parser state private
            // and describe exactly the object its Read/Write methods accept and produce.
            schema.Type = JsonSchemaType.Object;
            schema.Required = new HashSet<string>(["triageRevision"], StringComparer.Ordinal);
            schema.AdditionalPropertiesAllowed = false;
            schema.MinProperties = 2;
            schema.Description = "Required triageRevision and at least one editable field. Omitted fields are unchanged; explicit null clears fixedInVersion, issueUrl or notes. status cannot be null. Unknown or duplicate properties are invalid.";
            schema.Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["triageRevision"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64", Minimum = "0" },
                ["status"] = Strings(s_groupStatuses),
                ["fixedInVersion"] = NullableText(64, "Strict SemVer 2.0 version. Null clears the fixed version."),
                ["issueUrl"] = NullableText(2048, "Canonical https://github.com/<owner>/<repo>/issues/<positive-decimal> URL without credentials, port, query, fragment or trailing slash. Never fetched. Null clears the issue URL."),
                ["notes"] = NullableText(4096, "Plain, long-lived engineering notes; never include logs, names, paths or report material. No automatic report-to-note copy. Null clears the notes."),
            };
        }
        else if (type.Namespace == typeof(CrashReportDto).Namespace && schema.Properties is { } properties)
        {
            if (type == typeof(CrashGroupDto) || type == typeof(CrashGroupTriageDto))
                properties["status"] = Strings(s_groupStatuses, nullable: true);
            if (type == typeof(CrashReportDto))
                properties["status"] = Strings(s_reportStatuses, nullable: true);
            if (properties.ContainsKey("quality"))
                properties["quality"] = Quality();
            if (type == typeof(CrashGroupDetailDto))
            {
                OpenApiSchema quality = Quality();
                quality.Type |= JsonSchemaType.Null;
                properties["latestStackQuality"] = quality;
            }
        }
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<AuthorizeAttribute>().Any(attribute => attribute.Policy == CrashAdminPolicy.Name))
            return Task.CompletedTask;

        operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
        operation.Description += " Requires an account-backed Admin bearer session; PAT and cookie-only credentials are refused. All responses use Cache-Control: no-store. Disabled/unavailable analysis returns 503 only after authorization.";
        return Task.CompletedTask;
    }

    private static OpenApiSchema NullableText(int maximum, string description) => new()
    {
        Type = JsonSchemaType.String | JsonSchemaType.Null,
        MaxLength = maximum,
        Description = description,
    };

    private static OpenApiSchema Strings(IEnumerable<string> values, bool nullable = false)
    {
        var schema = new OpenApiSchema
        {
            Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String,
            Enum = [.. values.Select(value => JsonValue.Create(value))],
        };
        // JsonNode represents the JSON null literal with a null reference.
        if (nullable) schema.Enum.Add(null!);
        return schema;
    }

    private static OpenApiSchema Quality() => new()
    {
        Type = JsonSchemaType.Array,
        Items = Strings(s_quality),
        Description = "Known quality markers preserve these exact spellings; no flags produces an empty array. Unknown quality bits, trust or report enum values add unknownNativeValue.",
    };
}
