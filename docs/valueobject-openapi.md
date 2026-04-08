# ValueObject — OpenAPI Integration

With .NET 10's `Microsoft.AspNetCore.OpenApi` pipeline, `ValueObject<T>` types must be represented as their underlying scalar in the generated schema — not as wrapper objects like `{ value: 123 }`.

## Transformer Pattern

Implement `IOpenApiSchemaTransformer` (`ValueObjectOpenapiSchemaTransformer`):

- Register via `options.AddSchemaTransformer<ValueObjectOpenapiSchemaTransformer>()`
- During transformation: detect inheritance chain for `ValueObject<>`, clear the object schema shape, copy underlying primitive semantics (enum, number, string, etc.)

This produces clean scalar schemas. Runtime JSON serialization already emits raw primitive values via `ValueObjectJsonConverterFactory`.

> **Note:** Do not use legacy Swashbuckle methods (e.g., `GetOrCreateSchemaAsync`) — they are not compatible with the .NET 10 OpenAPI pipeline.
