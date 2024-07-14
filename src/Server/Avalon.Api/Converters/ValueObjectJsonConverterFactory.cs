using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Common;

namespace Avalon.Api.Converters;

public class ValueObjectJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return IsDerivedFromValueObject(typeToConvert);
    }

    private bool IsDerivedFromValueObject(Type? type)
    {
        while (type != null && type != typeof(object))
        {
            var baseType = type.BaseType;
            if (baseType is {IsGenericType: true} && baseType.GetGenericTypeDefinition() == typeof(ValueObject<>))
            {
                return true;
            }
            type = baseType;
        }
        return false;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.BaseType?.GetGenericArguments()[0];
        var converterType = typeof(ValueObjectJsonConverter<>).MakeGenericType(valueType!);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
