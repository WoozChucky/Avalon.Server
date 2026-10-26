using System.Collections;
using System.Reflection;
using Avalon.Api.Contract;
using Avalon.Common;
using Avalon.Network.Packets.Character;
using Avalon.World.Public.Characters;
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
    /// An item's instability is never sent or shown (#1180): no client learns how close an item is to bricking. No packet
    /// (<see cref="ItemSlotDto" />'s assembly), REST contract (<see cref="CharacterInventoryDto" />'s) or modding-API type
    /// (<see cref="InventoryItem" />'s) names it: not as a type, a property, a field, an enum value, a method, or a
    /// constructor or method parameter (a record's positional members among them). A member the serializers reach without
    /// its being public (a protobuf or JSON attribute on a private one) counts as public.
    /// </summary>
    [Fact]
    public void Name_instability_on_no_client_facing_type()
    {
        const string Hidden = "Instab";
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;
        List<string> offenders = [];

        foreach (Type type in new[] { typeof(CharacterInventoryDto), typeof(ItemSlotDto), typeof(InventoryItem) }
                     .Select(anchor => anchor.Assembly)
                     .Distinct()
                     .SelectMany(assembly => assembly.GetTypes())
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (type.Name.Contains(Hidden, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(type.FullName!);
            }

            foreach (MemberInfo member in type.GetMembers(Declared).Where(Reachable))
            {
                // A property's accessors are named by the property, which is listed itself.
                if (member is not MethodInfo { IsSpecialName: true } &&
                    member.Name.Contains(Hidden, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{type.Name}.{member.Name}");
                }

                if (member is not MethodBase method)
                {
                    continue;
                }

                foreach (ParameterInfo parameter in method.GetParameters()
                             .Where(p => p.Name?.Contains(Hidden, StringComparison.OrdinalIgnoreCase) == true))
                {
                    offenders.Add(method.IsConstructor
                        ? $"{type.Name}.{parameter.Name}"
                        : $"{type.Name}.{method.Name}.{parameter.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Instability must reach no client (#1180), yet these name it: " + string.Join(", ", offenders.Distinct()));
    }

    /// <summary>
    /// A member a client or a modder can reach: a public one, or one a serializer writes by attribute whatever its access.
    /// </summary>
    private static bool Reachable(MemberInfo member)
    {
        bool isPublic = member switch
        {
            FieldInfo field => field.IsPublic,
            MethodBase method => method.IsPublic,
            PropertyInfo property => property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true,
            EventInfo e => e.AddMethod?.IsPublic == true,
            Type nested => nested.IsNestedPublic,
            _ => false,
        };

        return isPublic || member.GetCustomAttributes(inherit: false).Any(attribute =>
            attribute.GetType().Name is "ProtoMemberAttribute" or "JsonIncludeAttribute" or "JsonPropertyNameAttribute");
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
