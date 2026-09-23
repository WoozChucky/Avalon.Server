using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalon.Api.Contract;
using Avalon.Common;
using Xunit;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// The REST surface carries primitives, never value objects. Every DTO takes the primitive and its
/// mapper unwraps with <c>.Value</c> — the same decision the packet contracts and the EF value
/// converters make at their own boundaries, so value objects live inside the server and stop at
/// every edge of it.
///
/// This is worth a test rather than a convention, because breaking it is silent. Expose an id as a
/// <c>CharacterId</c> and, with no converter registered for the API's serializer, it becomes
/// <c>{"value":42}</c> in every response carrying it and an object-shaped OpenAPI schema — a
/// breaking change to published API clients that no existing test notices.
///
/// If that is ever the intent, this test failing is the prompt to register
/// <c>Avalon.Common.Converters.ValueObjectJsonConverterFactory</c> on the API's JsonSerializerOptions
/// and to restore a schema transformer that flattens the value object to its primitive. Both were
/// removed once it became clear nothing on the surface needed them.
/// </summary>
public class ApiContractShould
{
    [Fact]
    public void Expose_Primitives_Rather_Than_Value_Objects()
    {
        List<string> offenders = [];

        foreach (Type type in typeof(CharacterInventoryDto).Assembly.GetTypes()
                     .Where(type => type.IsPublic)
                     .Where(type => type.Namespace?.StartsWith("Avalon.Api.Contract", StringComparison.Ordinal) == true)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(property => property.GetIndexParameters().Length == 0))
            {
                if (UnderlyingValueType(Unwrap(property.PropertyType)) is { } valueType)
                {
                    offenders.Add($"{type.Name}.{property.Name} is a value object over {valueType.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Looks through a nullable or a collection, so a <c>List&lt;CharacterId&gt;</c> or a
    /// <c>CharacterId?</c> is caught rather than walked past.
    /// </summary>
    private static Type Unwrap(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Unwrap(underlying);
        }

        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            if (type.IsArray && type.GetElementType() is { } element)
            {
                return Unwrap(element);
            }

            if (type.IsGenericType && type.GetGenericArguments() is [Type single])
            {
                return Unwrap(single);
            }
        }

        return type;
    }

    private static Type? UnderlyingValueType(Type? type)
    {
        for (Type? cursor = type; cursor != null; cursor = cursor.BaseType)
        {
            if (cursor.IsGenericType && cursor.GetGenericTypeDefinition() == typeof(ValueObject<>))
            {
                return cursor.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
