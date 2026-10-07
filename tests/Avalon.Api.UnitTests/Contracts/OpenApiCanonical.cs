using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// The form in which two OpenAPI documents are compared across the API split (#794): <c>servers</c> dropped, every
/// assembly qualifier in a schema key or a <c>$ref</c> replaced with <c>*</c> (a type moving to another assembly
/// changes only that), and <c>paths</c>, <c>components.schemas</c> and <c>tags</c> in ordinal order (which assembly
/// holds a controller changes only that). Everything else is compared as it is, its order included.
/// </summary>
public static partial class OpenApiCanonical
{
    /// <summary>How many differences <see cref="Differences"/> reports unless told otherwise.</summary>
    public const int DefaultLimit = 20;

    private const string Ref = "$ref";

    /// <summary>
    /// <paramref name="name"/> with each assembly qualifier (<c>, Assembly, Version=..., Culture=...,
    /// PublicKeyToken=...</c>) replaced with <c>, *</c>.
    /// </summary>
    public static string WithoutAssemblyQualifiers(string name) => AssemblyQualifier().Replace(name, ", *");

    /// <summary>A canonical copy of <paramref name="document"/>; the document itself is left as it is.</summary>
    public static JsonObject Canonicalise(JsonNode document)
    {
        if (document is not JsonObject root)
        {
            throw new ArgumentException("An OpenAPI document is a JSON object.", nameof(document));
        }

        var canonical = new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> property in root)
        {
            switch (property.Key)
            {
                case "servers":
                    break;
                case "paths":
                    canonical[property.Key] = Sorted(property.Value, key => key);
                    break;
                case "components":
                    canonical[property.Key] = Components(property.Value);
                    break;
                case "tags":
                    canonical[property.Key] = Tags(property.Value);
                    break;
                default:
                    canonical[property.Key] = Copy(property.Value);
                    break;
            }
        }

        return canonical;
    }

    /// <summary>
    /// The JSON pointers at which <paramref name="actual"/> differs from <paramref name="expected"/>, in document
    /// order, at most <paramref name="limit"/>: a value that differs, a member or element only one side has, or an
    /// object whose members are the same but in another order.
    /// </summary>
    public static IReadOnlyList<string> Differences(JsonNode? expected, JsonNode? actual, int limit = DefaultLimit)
    {
        var found = new List<string>();
        Compare(expected, actual, string.Empty, found, limit);
        return found;
    }

    /// <summary>The JSON pointer reference token for <paramref name="key"/> (RFC 6901).</summary>
    public static string Escape(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static JsonNode? Components(JsonNode? components)
    {
        if (components is not JsonObject members)
        {
            return Copy(components);
        }

        var copy = new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> member in members)
        {
            copy[member.Key] = member.Key == "schemas" ? Sorted(member.Value, WithoutAssemblyQualifiers) : Copy(member.Value);
        }

        return copy;
    }

    /// <summary>A copy of an object with its keys renamed by <paramref name="rename"/>, in ordinal order.</summary>
    private static JsonNode? Sorted(JsonNode? node, Func<string, string> rename)
    {
        if (node is not JsonObject members)
        {
            return Copy(node);
        }

        var renamed = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> member in members)
        {
            string key = rename(member.Key);
            if (!renamed.TryAdd(key, Copy(member.Value)))
            {
                throw new InvalidOperationException($"Two members canonicalise to the same key {key}.");
            }
        }

        var sorted = new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> member in renamed)
        {
            sorted[member.Key] = member.Value;
        }

        return sorted;
    }

    private static JsonNode? Tags(JsonNode? tags)
    {
        if (tags is not JsonArray elements)
        {
            return Copy(tags);
        }

        return new JsonArray(elements
            .OrderBy(tag => (tag as JsonObject)?["name"]?.GetValue<string>(), StringComparer.Ordinal)
            .Select(Copy)
            .ToArray());
    }

    /// <summary>A deep copy in which every <c>$ref</c> loses its assembly qualifiers.</summary>
    private static JsonNode? Copy(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                var copy = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> member in members)
                {
                    copy[member.Key] = member.Key == Ref && member.Value is JsonValue reference
                                       && reference.TryGetValue(out string? target)
                        ? JsonValue.Create(WithoutAssemblyQualifiers(target))
                        : Copy(member.Value);
                }

                return copy;
            case JsonArray elements:
                return new JsonArray(elements.Select(Copy).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    private static void Compare(JsonNode? expected, JsonNode? actual, string pointer, List<string> found, int limit)
    {
        if (found.Count >= limit)
        {
            return;
        }

        switch (expected, actual)
        {
            case (JsonObject expectedMembers, JsonObject actualMembers):
                CompareObjects(expectedMembers, actualMembers, pointer, found, limit);
                break;
            case (JsonArray expectedElements, JsonArray actualElements):
                for (int i = 0; i < Math.Max(expectedElements.Count, actualElements.Count) && found.Count < limit; i++)
                {
                    string element = pointer + "/" + i;
                    if (i >= expectedElements.Count || i >= actualElements.Count)
                    {
                        found.Add(element);
                    }
                    else
                    {
                        Compare(expectedElements[i], actualElements[i], element, found, limit);
                    }
                }

                break;
            default:
                if (!JsonNode.DeepEquals(expected, actual))
                {
                    found.Add(pointer);
                }

                break;
        }
    }

    private static void CompareObjects(JsonObject expected, JsonObject actual, string pointer, List<string> found, int limit)
    {
        bool sameMembers = true;
        foreach (KeyValuePair<string, JsonNode?> member in expected)
        {
            if (found.Count >= limit)
            {
                return;
            }

            string child = pointer + "/" + Escape(member.Key);
            if (actual.TryGetPropertyValue(member.Key, out JsonNode? other))
            {
                Compare(member.Value, other, child, found, limit);
            }
            else
            {
                sameMembers = false;
                found.Add(child);
            }
        }

        foreach (KeyValuePair<string, JsonNode?> member in actual)
        {
            if (found.Count >= limit)
            {
                return;
            }

            if (!expected.ContainsKey(member.Key))
            {
                sameMembers = false;
                found.Add(pointer + "/" + Escape(member.Key));
            }
        }

        if (sameMembers && found.Count < limit
            && !expected.Select(member => member.Key).SequenceEqual(actual.Select(member => member.Key), StringComparer.Ordinal))
        {
            found.Add(pointer);
        }
    }

    [GeneratedRegex(@", [^,\[\]]+, Version=[^,\[\]]*, Culture=[^,\[\]]*, PublicKeyToken=[^,\[\]]*")]
    private static partial Regex AssemblyQualifier();
}
