using System.Text.Json;
using Avalon.Api.Hosting;
using Avalon.Api.Testing;
using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Crashes;

public sealed class CrashOpenApiFixture : IAsyncLifetime
{
    private ApiTestHost _host = null!;
    public JsonDocument Document { get; private set; } = null!;
    public int ConnectionsCreated { get; private set; }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?>
            {
                [ApiDocs.EnabledSetting] = "true",
                ["Application:Crashes:Enabled"] = "false",
                ["Database:Crashes:ConnectionString"] = null,
            },
            Configure = services => services.AddSingleton<IDbContextFactory<CrashesDbContext>>(_ =>
            {
                ConnectionsCreated++;
                throw new InvalidOperationException("OpenAPI must not initialize crash persistence.");
            }),
        });
        Document = JsonDocument.Parse(await _host.Client.GetStringAsync("/openapi/v1.json"));
    }

    public async Task DisposeAsync()
    {
        Document.Dispose();
        await _host.DisposeAsync();
    }
}

public sealed class CrashOpenApiShould(CrashOpenApiFixture fixture) : IClassFixture<CrashOpenApiFixture>
{
    private JsonElement Paths => fixture.Document.RootElement.GetProperty("paths");
    private JsonElement Schemas => fixture.Document.RootElement.GetProperty("components").GetProperty("schemas");
    private JsonElement Schema(string name) => Schemas.GetProperty("Avalon.Api.Contract.Crashes." + name);
    private JsonElement Operation(string path, string method = "get") => Paths.GetProperty("/admin/crashes/" + path).GetProperty(method);

    private JsonElement Resolve(JsonElement schema) => schema.TryGetProperty("$ref", out JsonElement reference)
        ? Schemas.GetProperty(reference.GetString()!.Split('/')[^1]) : schema;

    private void Primitive(JsonElement schema, string type, string? format = null, bool nullable = false)
    {
        schema = Resolve(schema);
        if (schema.TryGetProperty("oneOf", out JsonElement alternatives))
        {
            Assert.Equal(nullable, alternatives.EnumerateArray().Any(item => item.TryGetProperty("type", out JsonElement kind) && kind.GetString() == "null"));
            Primitive(alternatives.EnumerateArray().Single(item => !item.TryGetProperty("type", out JsonElement kind) || kind.GetString() != "null"), type, format);
            return;
        }
        string[] types = schema.GetProperty("type").ValueKind == JsonValueKind.Array
            ? schema.GetProperty("type").EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [schema.GetProperty("type").GetString()!];
        Assert.Contains(type, types);
        Assert.Equal(nullable, types.Contains("null", StringComparer.Ordinal));
        if (format is not null) Assert.Equal(format, schema.GetProperty("format").GetString());
    }

    [Fact]
    public void Describe_all_four_operations_without_initializing_crash_storage()
    {
        Assert.Equal("ListCrashGroups", Operation("groups").GetProperty("operationId").GetString());
        Assert.Equal("GetCrashGroup", Operation("groups/{id}").GetProperty("operationId").GetString());
        Assert.Equal("UpdateCrashGroup", Operation("groups/{id}", "patch").GetProperty("operationId").GetString());
        Assert.Equal("GetCrashReport", Operation("reports/{id}").GetProperty("operationId").GetString());
        Assert.Equal(3, Paths.EnumerateObject().Count(path => path.Name.StartsWith("/admin/crashes/", StringComparison.Ordinal)));
        Assert.Equal(0, fixture.ConnectionsCreated);
    }

    [Theory]
    [InlineData("groups", "get")]
    [InlineData("groups/{id}", "get")]
    [InlineData("groups/{id}", "patch")]
    [InlineData("reports/{id}", "get")]
    public void Require_an_admin_bearer_session_in_the_generated_contract(string path, string method)
    {
        JsonElement operation = Operation(path, method);
        JsonElement requirement = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.Equal("Bearer", Assert.Single(requirement.EnumerateObject()).Name);
        Assert.Contains("PAT", operation.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("groups", "get", "CrashGroupPageDto")]
    [InlineData("groups/{id}", "get", "CrashGroupDetailDto")]
    [InlineData("groups/{id}", "patch", "CrashGroupTriageDto")]
    [InlineData("reports/{id}", "get", "CrashReportDto")]
    public void Name_success_responses_and_describe_feature_errors_as_problem_json(string path, string method, string dto)
    {
        JsonElement responses = Operation(path, method).GetProperty("responses");
        Assert.EndsWith("/Avalon.Api.Contract.Crashes." + dto,
            responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        foreach (string status in new[] { "400", "404", "409", "503" })
        {
            if (responses.TryGetProperty(status, out JsonElement response))
                Assert.Equal("application/problem+json", Assert.Single(response.GetProperty("content").EnumerateObject()).Name);
        }
    }

    [Fact]
    public void Describe_patch_presence_and_nullability_in_a_named_object_schema()
    {
        JsonElement body = Operation("groups/{id}", "patch").GetProperty("requestBody");
        Assert.True(body.GetProperty("required").GetBoolean());
        Assert.EndsWith("/Avalon.Api.Contract.Crashes.UpdateCrashGroupRequest",
            body.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        JsonElement schema = Schema("UpdateCrashGroupRequest");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(["triageRevision"], schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(2, schema.GetProperty("minProperties").GetInt32());
        JsonElement properties = schema.GetProperty("properties");
        Assert.Equal(["fixedInVersion", "issueUrl", "notes", "status", "triageRevision"],
            properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Primitive(properties.GetProperty("triageRevision"), "integer", "int64");
        Assert.Equal(0, properties.GetProperty("triageRevision").GetProperty("minimum").GetInt64());
        Primitive(properties.GetProperty("status"), "string");
        Assert.Equal(["new", "investigating", "fixed", "ignored"],
            properties.GetProperty("status").GetProperty("enum").EnumerateArray().Select(item => item.GetString()));
        foreach (string field in new[] { "fixedInVersion", "issueUrl", "notes" })
            Primitive(properties.GetProperty(field), "string", nullable: true);
        Assert.Equal(4096, properties.GetProperty("notes").GetProperty("maxLength").GetInt32());
        Assert.Equal(2048, properties.GetProperty("issueUrl").GetProperty("maxLength").GetInt32());
        Assert.Equal(64, properties.GetProperty("fixedInVersion").GetProperty("maxLength").GetInt32());
        string description = Operation("groups/{id}", "patch").GetProperty("description").GetString()!;
        Assert.Contains("Omitted fields are unchanged; explicit null clears nullable fields", description, StringComparison.Ordinal);
        Assert.Contains("never include logs, names, paths or report material", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserve_primitive_uuid_dates_pagination_and_hex_addresses()
    {
        foreach (string path in new[] { "groups/{id}", "reports/{id}" })
        {
            JsonElement id = Operation(path).GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "id");
            Assert.True(id.GetProperty("required").GetBoolean());
            Primitive(id.GetProperty("schema"), "string", "uuid");
        }
        foreach ((string path, string[] names) in new[] { ("groups", new[] { "page", "pageSize" }), ("groups/{id}", new[] { "reportsPage", "reportsPageSize" }) })
        {
            foreach (string name in names)
                Primitive(Operation(path).GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == name).GetProperty("schema"), "integer", "int32");
        }
        Primitive(Operation("groups").GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "since").GetProperty("schema"), "string", "date-time");
        Primitive(Schema("CrashGroupDto").GetProperty("properties").GetProperty("id"), "string", "uuid");
        Primitive(Schema("CrashReportDto").GetProperty("properties").GetProperty("id"), "string", "uuid");
        Primitive(Schema("CrashReportDto").GetProperty("properties").GetProperty("groupId"), "string", "uuid", nullable: true);
        Primitive(Schema("CrashReportDto").GetProperty("properties").GetProperty("receivedAt"), "string", "date-time");
        Primitive(Schema("CrashDailyCountDto").GetProperty("properties").GetProperty("date"), "string", "date");
        foreach ((string dto, string field, bool nullable) in new[]
        {
            ("CrashReportDto", "exceptionAddress", true), ("CrashIdentityDto", "address", true),
            ("CrashFrameDto", "moduleOffset", true), ("CrashModuleDto", "baseAddress", true),
            ("CrashModuleDto", "size", true), ("CrashMetadataDto", "exceptionAddress", false),
        })
        {
            Primitive(Schema(dto).GetProperty("properties").GetProperty(field), "string", nullable: nullable);
        }
    }

    [Fact]
    public void Name_every_stack_shape_and_keep_nullable_report_content_explicit()
    {
        foreach (string name in new[] { "CrashStackDto", "CrashIdentityDto", "CrashSystemDto", "CrashThreadDto", "CrashFrameDto", "CrashInlineFrameDto", "CrashModuleDto", "CrashMetadataDto", "CrashProcessorDto" })
            Assert.Equal("object", Schema(name).GetProperty("type").GetString());
        JsonElement report = Schema("CrashReportDto").GetProperty("properties");
        Assert.Contains("CrashStackDto", report.GetProperty("analysis").GetRawText(), StringComparison.Ordinal);
        Primitive(report.GetProperty("logTail"), "string", nullable: true);
        Assert.Contains("CrashThreadDto", Schema("CrashGroupDetailDto").GetProperty("properties").GetProperty("latestStack").GetRawText(), StringComparison.Ordinal);
        Assert.False(Schema("CrashStackDto").GetProperty("properties").TryGetProperty("logTail", out _));
    }

    [Fact]
    public void Document_lifetime_retained_expired_and_status_semantics()
    {
        Assert.Contains("lifetime", Operation("groups").GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retained", Operation("groups").GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("null", Operation("groups/{id}").GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expire", Operation("groups/{id}").GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        string report = Operation("reports/{id}").GetProperty("description").GetString()!;
        Assert.Contains("CRASH_REPORT_NOT_AVAILABLE", report, StringComparison.Ordinal);
        Assert.Contains("This report has expired or is unavailable.", report, StringComparison.Ordinal);
        Assert.Contains("retry_wait", Schema("CrashReportDto").GetProperty("properties").GetProperty("status").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("investigating", Schema("CrashGroupDto").GetProperty("properties").GetProperty("status").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("missingSystemSymbols", Schema("CrashStackDto").GetProperty("properties").GetProperty("quality").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("sanitizedText", Schema("CrashStackDto").GetProperty("properties").GetProperty("quality").GetRawText(), StringComparison.Ordinal);
    }
}
