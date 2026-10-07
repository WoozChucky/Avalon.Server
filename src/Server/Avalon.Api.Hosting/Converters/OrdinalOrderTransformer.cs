using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Avalon.Api.Hosting.Converters;

/// <summary>
/// Puts the document's paths, tags and schemas in ordinal order (#794, design section 3.1), so the document a process
/// serves does not depend on which assembly holds a controller or in which order the services were registered. The
/// last document transformer: it orders what the others added.
/// </summary>
internal sealed class OrdinalOrderTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Paths is { } paths)
        {
            var ordered = new OpenApiPaths();
            foreach (KeyValuePair<string, IOpenApiPathItem> path in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
                ordered.Add(path.Key, path.Value);
            if (paths.Extensions is { } extensions)
                ordered.Extensions = extensions;
            document.Paths = ordered;
        }

        if (document.Tags is { } tags)
        {
            document.Tags = new HashSet<OpenApiTag>(tags.OrderBy(t => t.Name, StringComparer.Ordinal),
                (tags as HashSet<OpenApiTag>)?.Comparer);
        }

        if (document.Components?.Schemas is { } schemas)
        {
            document.Components.Schemas = schemas.OrderBy(s => s.Key, StringComparer.Ordinal)
                .ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        }

        return Task.CompletedTask;
    }
}
