using System.Globalization;
using System.Text;

namespace Avalon.SchemaGen;

/// <summary>
/// Writes a fixture's values above its bytes, so that the corpus can be read rather than only
/// compared. Nothing here feeds the encoding; it is the annotation that makes a diff mean
/// something to whoever reads it.
/// </summary>
internal static class FixtureRendering
{
    internal static void WriteMembers(StringBuilder text, Type contract, object fixture, int indent)
    {
        foreach (WireMember member in WireFixtures.Members(contract))
        {
            object? value = member.Read(fixture);

            text.Append(' ', indent)
                .Append("field ")
                .Append(member.Tag.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(member.Name)
                .Append(" : ")
                .Append(TypeName(member.DeclaredType));

            WriteValue(text, member.DeclaredType, value, indent);
        }
    }

    private static void WriteValue(StringBuilder text, Type declared, object? value, int indent)
    {
        if (value is null)
        {
            text.Append(" = null\n");
            return;
        }

        if (FixtureValues.ElementTypeOf(declared) is { } element)
        {
            WriteElements(text, element, (System.Collections.IEnumerable)value, indent);
            return;
        }

        if (FixtureValues.IsContract(declared))
        {
            text.Append('\n');
            WriteMembers(text, declared, value, indent + 2);
            return;
        }

        text.Append(" = ").Append(Scalar(value)).Append('\n');
    }

    private static void WriteElements(StringBuilder text, Type element, System.Collections.IEnumerable items, int indent)
    {
        object?[] materialized = items.Cast<object?>().ToArray();

        text.Append(" = ")
            .Append(materialized.Length.ToString(CultureInfo.InvariantCulture))
            .Append(materialized.Length == 1 ? " element\n" : " elements\n");

        for (int i = 0; i < materialized.Length; i++)
        {
            text.Append(' ', indent + 2)
                .Append("element ")
                .Append(i.ToString(CultureInfo.InvariantCulture));

            WriteValue(text, element, materialized[i], indent + 2);
        }
    }

    private static string Scalar(object value) => value switch
    {
        bool flag => flag ? "true" : "false",
        string text => Text(text),
        byte[] bytes => Binary(bytes),
        ReadOnlyMemory<byte> memory => Binary(memory.ToArray()),
        float number => Float(number),
        DateTime moment => $"{moment.ToString("O", CultureInfo.InvariantCulture)} ({moment.Kind})",
        Guid identifier => identifier.ToString("D", CultureInfo.InvariantCulture),
        Enum member => Enumeration(member),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "?",
    };

    /// <summary>
    /// The float's decimal form alongside its bits, because the bits are the part the wire
    /// carries and the part a decimal rendering of NaN or a denormal cannot pin down.
    /// </summary>
    private static string Float(float number)
    {
        string bits = BitConverter.SingleToInt32Bits(number).ToString("x8", CultureInfo.InvariantCulture);
        string shown = float.IsNegative(number) && number == 0f
            ? "-0"
            : number.ToString("G9", CultureInfo.InvariantCulture);

        return $"{shown} (0x{bits})";
    }

    private static string Enumeration(Enum member)
    {
        string label = member.ToString();
        string numeric = Convert.ToInt64(member, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

        return string.Equals(label, numeric, StringComparison.Ordinal)
            ? $"<not a declared member> ({numeric})"
            : $"{label} ({numeric})";
    }

    private static string Binary(byte[] bytes)
    {
        const int preview = 8;

        var text = new StringBuilder()
            .Append(bytes.Length.ToString(CultureInfo.InvariantCulture))
            .Append(bytes.Length == 1 ? " byte" : " bytes");

        for (int i = 0; i < Math.Min(preview, bytes.Length); i++)
        {
            text.Append(' ').Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return bytes.Length > preview ? text.Append(" ...").ToString() : text.ToString();
    }

    private static string Text(string value)
    {
        const int preview = 48;

        var escaped = new StringBuilder("\"");
        int shown = 0;

        foreach (char c in value)
        {
            if (shown >= preview)
            {
                escaped.Append('…');
                break;
            }

            shown++;
            escaped.Append(Escape(c));
        }

        escaped.Append('"');

        int utf8 = Encoding.UTF8.GetByteCount(value);

        return $"{escaped} ({value.Length} chars, {utf8.ToString(CultureInfo.InvariantCulture)} bytes utf-8)";
    }

    private static string Escape(char c) => c switch
    {
        '"' => "\\\"",
        '\\' => "\\\\",
        '\0' => "\\0",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        < ' ' or (char)0x7f => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
        _ => c.ToString(),
    };

    private static string TypeName(Type declared)
    {
        if (Nullable.GetUnderlyingType(declared) is { } underlying)
        {
            return TypeName(underlying) + "?";
        }

        if (declared.IsArray && declared != typeof(byte[]))
        {
            return TypeName(declared.GetElementType()!) + "[]";
        }

        if (declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(List<>))
        {
            return $"List<{TypeName(declared.GetGenericArguments()[0])}>";
        }

        if (declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>))
        {
            return $"ReadOnlyMemory<{TypeName(declared.GetGenericArguments()[0])}>";
        }

        return Keywords.TryGetValue(declared, out string? keyword) ? keyword : declared.Name;
    }

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(string)] = "string",
        [typeof(byte[])] = "byte[]",
    };
}
