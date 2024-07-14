using Avalon.Common;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Avalon.Api.Converters;

public class ValueObjectSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (IsValueObject(context.Type))
        {
            var valueType = context.Type.BaseType?.GetGenericArguments()[0];
            var valueSchema = context.SchemaGenerator.GenerateSchema(valueType, context.SchemaRepository);
            schema.Type = valueSchema.Type;
            schema.Format = valueSchema.Format;
            schema.Example = valueSchema.Example;
            schema.Properties.Clear();
            schema.Reference = valueSchema.Reference;
            schema.AllOf.Clear();
            schema.OneOf.Clear();
            schema.AnyOf.Clear();
        }
    }

    private bool IsValueObject(Type? type)
    {
        while (type != null && type != typeof(object))
        {
            var baseType = type.BaseType;
            if (baseType != null && baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(ValueObject<>))
            {
                return true;
            }
            type = baseType;
        }
        return false;
    }
}
