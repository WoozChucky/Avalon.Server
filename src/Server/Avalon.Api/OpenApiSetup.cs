using Avalon.Api.Converters;
using Microsoft.OpenApi;

namespace Avalon.Api;

/// <summary>
/// The OpenAPI document the API serves at <c>/openapi/v1.json</c> and the docs build publishes. Program.cs registers
/// it, and so does ContractGoldenShould, which holds the served document to the one published before the API split
/// (#794).
/// </summary>
public static class OpenApiSetup
{
    /// <summary>The document's info, its bearer security scheme, its schema transformers and its schema names.</summary>
    public static IServiceCollection AddAvalonOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, arg3) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Avalon.Api",
                    Version = "v1",
                    Description = "The official API for Avalon.",
                    Contact = new OpenApiContact { Name = "Avalon Project", Url = new Uri("https://avalon.monster") },
                    License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://opensource.org/license/mit/") },
                    TermsOfService = new Uri("https://avalon.monster/terms")
                };
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddSchemaTransformer((schema, context, cancellationToken) =>
            {
                // The shared System.String reference drops per-property annotations. Keep this proof's
                // bounded wire schema inline, without changing other string contracts.
                if (context.JsonTypeInfo.Type == typeof(Avalon.Api.Contract.AccountEmailVerificationConfirmRequest))
                {
                    schema.Properties!["token"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        MinLength = 43,
                        MaxLength = 43,
                        Pattern = "^[A-Za-z0-9_-]{43}$",
                    };
                }
                // Preserve the CLR uint32 bounds, including nullable request selectors.
                if (context.JsonTypeInfo.Type == typeof(uint) || context.JsonTypeInfo.Type == typeof(uint?))
                {
                    schema.Minimum = "0";
                    schema.Maximum = "4294967295";
                }
                return Task.CompletedTask;
            });
            options.CreateSchemaReferenceId = type => type.Type.FullName!;
        });
}
