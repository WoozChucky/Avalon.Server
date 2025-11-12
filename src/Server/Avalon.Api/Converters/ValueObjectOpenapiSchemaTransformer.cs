// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Avalon.Common;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Avalon.Api.Converters;

public class ValueObjectOpenapiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Only touch schemas for ValueObject<T>
        if (!IsValueObject(context.JsonTypeInfo.Type))
        {
            return Task.CompletedTask;
        }

        // Get the underlying T from ValueObject<T>
        Type? valueType = context.JsonTypeInfo.Type.BaseType?.GetGenericArguments()[0];
        if (valueType is null)
        {
            return Task.CompletedTask;
        }

        // Reset the object-y bits
        schema.AllOf?.Clear();
        schema.OneOf?.Clear();
        schema.AnyOf?.Clear();
        schema.Properties?.Clear();

        // Heuristic: produce an inline scalar schema for T
        CopyScalarShape(valueType, schema, GetJsonOptions(context));
        return Task.CompletedTask;
    }

    private static JsonSerializerOptions GetJsonOptions(OpenApiSchemaTransformerContext ctx) =>
        ctx.ApplicationServices.GetService(typeof(IOptions<JsonOptions>)) is IOptions<JsonOptions> o
            ? o.Value.SerializerOptions
            : new JsonSerializerOptions();

    private static void CopyScalarShape(Type t, OpenApiSchema schema, JsonSerializerOptions json)
    {
        // enums -> prefer string names if JsonStringEnumConverter is registered
        if (t.IsEnum)
        {
            bool useString = json.Converters.Any(c => c is JsonStringEnumConverter);
            if (useString)
            {
                schema.Type = JsonSchemaType.String;
                schema.Enum = Enum.GetNames(t).Select(JsonNode? (n) => JsonValue.Create(n)).ToList()!;
            }
            else
            {
                // Numeric enums (best-effort; using JsonValue for integers)
                schema.Type = JsonSchemaType.Integer;
                schema.Format = "int32";
                schema.Enum = Enum.GetValues(t).Cast<object>()
                    .Select(JsonNode? (v) => JsonValue.Create(Convert.ToInt32(v)))
                    .ToList()!;
            }

            return;
        }

        // Primitives & common BCL “value” types
        if (t == typeof(string))
        {
            schema.Type = JsonSchemaType.String;
            return;
        }

        if (t == typeof(char))
        {
            schema.Type = JsonSchemaType.String;
            schema.MinLength = 1;
            schema.MaxLength = 1;
            return;
        }

        if (t == typeof(bool))
        {
            schema.Type = JsonSchemaType.Boolean;
            return;
        }

        if (t == typeof(byte[]))
        {
            schema.Type = JsonSchemaType.Array;
            schema.Format = "byte";
            return;
        }

        if (t == typeof(Guid))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uuid";
            return;
        }

        if (t == typeof(Uri))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uri";
            return;
        } // allowed format in OAS 3.1

        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date-time";
            return;
        }

        if (t == typeof(DateOnly))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date";
            return;
        }

        if (t == typeof(TimeOnly))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "time";
            return;
        }

        if (t == typeof(float))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "float";
            return;
        }

        if (t == typeof(double))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "double";
            return;
        }

        if (t == typeof(decimal))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "decimal";
            return;
        } // custom format is fine

        if (t == typeof(sbyte))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "int32";
            schema.Minimum = "-128";
            schema.Maximum = 127.ToString();
            return;
        }

        if (t == typeof(byte))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "int32";
            schema.Minimum = 0.ToString();
            schema.Maximum = 255.ToString();
            return;
        }

        if (t == typeof(short))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "int32";
            schema.Minimum = short.MinValue.ToString();
            schema.Maximum = short.MaxValue.ToString();
            return;
        }

        if (t == typeof(ushort))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "int32";
            schema.Minimum = 0.ToString();
            schema.Maximum = ushort.MaxValue.ToString();
            return;
        }

        if (t == typeof(int))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "int32";
            return;
        }

        if (t == typeof(uint))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "int64";
            schema.Minimum = 0.ToString();
            schema.Maximum = 4294967295.ToString();
            return;
        }

        if (t == typeof(long))
        {
            schema.Type = JsonSchemaType.Number;
            schema.Format = "int64";
            return;
        }

        if (t == typeof(ulong))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "uint64";
            return;
        } // safer than overflowing int64

        // Fallback: inline object with no properties (least-wrong)
        schema.Type = JsonSchemaType.Object;
    }

    private static bool IsValueObject(Type? type)
    {
        while (type is not null && type != typeof(object))
        {
            Type? baseType = type.BaseType;
            if (baseType is not null &&
                baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition() == typeof(ValueObject<>))
            {
                return true;
            }

            type = baseType;
        }

        return false;
    }
}
