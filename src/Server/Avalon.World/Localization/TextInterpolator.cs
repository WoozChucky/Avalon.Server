using System.Text;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Localization;

namespace Avalon.World.Localization;

/// <summary>
/// Substitutes named tokens and resolves gender selects in a localised string.
/// </summary>
/// <remarks>
/// <para>
/// Named tokens rather than positional <c>{0}</c>: word order is not stable across languages, and a
/// translator reordering positional arguments has to track indices with nothing to catch a mistake.
/// <c>{name}</c> can sit wherever a language's grammar wants it.
/// </para>
/// <para>
/// Malformed input renders verbatim rather than throwing. This runs on the tick thread, for every
/// dialogue packet, and broken content must not be able to take a map down. An unknown token stays
/// as <c>{nmae}</c> — visibly wrong and traceable, which beats a silent gap. The seed tests are the
/// real defence.
/// </para>
/// </remarks>
public static class TextInterpolator
{
    private const string GenderPrefix = "g:";

    public static string Resolve(string template, in TextContext context)
    {
        if (string.IsNullOrEmpty(template)) return template;
        if (template.IndexOf('{') < 0) return template;

        var result = new StringBuilder(template.Length + 16);

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];

            if (c != '{')
            {
                result.Append(c);
                continue;
            }

            // "{{" is a literal brace.
            if (i + 1 < template.Length && template[i + 1] == '{')
            {
                result.Append('{');
                i++;
                continue;
            }

            int close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                // Unterminated: the rest is literal.
                result.Append(template, i, template.Length - i);
                break;
            }

            string body = template[(i + 1)..close];

            if (TryResolve(body, context, out string? replacement))
            {
                result.Append(replacement);
            }
            else
            {
                // Unknown or malformed — emit the construct as written.
                result.Append('{').Append(body).Append('}');
            }

            i = close;
        }

        return result.ToString();
    }

    private static bool TryResolve(string body, in TextContext context, out string? replacement)
    {
        if (body.StartsWith(GenderPrefix, StringComparison.Ordinal))
        {
            return TryResolveGender(body[GenderPrefix.Length..], context.PlayerGender, out replacement);
        }

        switch (body)
        {
            case "name":
                replacement = context.PlayerName;
                return true;
            case "class":
                replacement = context.PlayerClassName;
                return true;
            case "level":
                replacement = context.PlayerLevel.ToString();
                return true;
            default:
                replacement = null;
                return false;
        }
    }

    /// <summary>
    /// <c>{g:male|female}</c>. Either branch may be empty — the Portuguese masculine of
    /// "Caçador{g:|a}" takes no suffix — but there must be exactly one separator. Branches hold
    /// literal text only: no nested tokens, no nested selects.
    /// </summary>
    private static bool TryResolveGender(string branches, CharacterGender gender, out string? replacement)
    {
        int separator = branches.IndexOf('|');
        if (separator < 0 || branches.IndexOf('|', separator + 1) >= 0)
        {
            replacement = null;
            return false;
        }

        replacement = gender == CharacterGender.Female
            ? branches[(separator + 1)..]
            : branches[..separator];

        return true;
    }
}
