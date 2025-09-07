// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Common;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Avalon.Api.Converters;

public class ValueObjectOpenapiSchemaTransformer : IOpenApiSchemaTransformer
{
    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Only touch schemas for ValueObject<T>
        if (!IsValueObject(context.JsonTypeInfo.Type))
        {
            return;
        }

        // Get the underlying T from ValueObject<T>
        Type? valueType = context.JsonTypeInfo.Type.BaseType?.GetGenericArguments()[0];
        if (valueType is null)
        {
            return;
        }

        // Reset the object-y bits
        schema.AllOf.Clear();
        schema.OneOf.Clear();
        schema.AnyOf.Clear();
        schema.Properties.Clear();
        schema.Reference = null;

        // Heuristic: produce an inline scalar schema for T
        CopyScalarShape(valueType, schema, GetJsonOptions(context));
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
                schema.Type = "string";
                schema.Enum = Enum.GetNames(t).Select(n => (IOpenApiAny)new OpenApiString(n)).ToList();
            }
            else
            {
                // Numeric enums (best-effort; OpenAPI.NET only has OpenApiInteger)
                schema.Type = "integer";
                schema.Format = "int32";
                schema.Enum = Enum.GetValues(t).Cast<object>()
                    .Select(v => (IOpenApiAny)new OpenApiInteger(Convert.ToInt32(v)))
                    .ToList();
            }

            return;
        }

        // Primitives & common BCL “value” types
        if (t == typeof(string))
        {
            schema.Type = "string";
            return;
        }

        if (t == typeof(char))
        {
            schema.Type = "string";
            schema.MinLength = 1;
            schema.MaxLength = 1;
            return;
        }

        if (t == typeof(bool))
        {
            schema.Type = "boolean";
            return;
        }

        if (t == typeof(byte[]))
        {
            schema.Type = "string";
            schema.Format = "byte";
            return;
        }

        if (t == typeof(Guid))
        {
            schema.Type = "string";
            schema.Format = "uuid";
            return;
        }

        if (t == typeof(Uri))
        {
            schema.Type = "string";
            schema.Format = "uri";
            return;
        } // allowed format in OAS 3.1

        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
        {
            schema.Type = "string";
            schema.Format = "date-time";
            return;
        }

        if (t == typeof(DateOnly))
        {
            schema.Type = "string";
            schema.Format = "date";
            return;
        }

        if (t == typeof(TimeOnly))
        {
            schema.Type = "string";
            schema.Format = "time";
            return;
        }

        if (t == typeof(float))
        {
            schema.Type = "number";
            schema.Format = "float";
            return;
        }

        if (t == typeof(double))
        {
            schema.Type = "number";
            schema.Format = "double";
            return;
        }

        if (t == typeof(decimal))
        {
            schema.Type = "number";
            schema.Format = "decimal";
            return;
        } // custom format is fine

        if (t == typeof(sbyte))
        {
            schema.Type = "integer";
            schema.Format = "int32";
            schema.Minimum = -128;
            schema.Maximum = 127;
            return;
        }

        if (t == typeof(byte))
        {
            schema.Type = "integer";
            schema.Format = "int32";
            schema.Minimum = 0;
            schema.Maximum = 255;
            return;
        }

        if (t == typeof(short))
        {
            schema.Type = "integer";
            schema.Format = "int32";
            schema.Minimum = short.MinValue;
            schema.Maximum = short.MaxValue;
            return;
        }

        if (t == typeof(ushort))
        {
            schema.Type = "integer";
            schema.Format = "int32";
            schema.Minimum = 0;
            schema.Maximum = ushort.MaxValue;
            return;
        }

        if (t == typeof(int))
        {
            schema.Type = "integer";
            schema.Format = "int32";
            return;
        }

        if (t == typeof(uint))
        {
            schema.Type = "integer";
            schema.Format = "int64";
            schema.Minimum = 0;
            schema.Maximum = 4294967295;
            return;
        }

        if (t == typeof(long))
        {
            schema.Type = "integer";
            schema.Format = "int64";
            return;
        }

        if (t == typeof(ulong))
        {
            schema.Type = "string";
            schema.Format = "uint64";
            return;
        } // safer than overflowing int64

        // Fallback: inline object with no properties (least-wrong)
        schema.Type = "object";
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
