using System.Text;

namespace Avalon.Common.Utils;

/// <summary>
/// The game client's rule for a name the server sends that becomes one file's name inside a folder (a model key, a
/// model kit, an animation clip): a plain name never reaches past that folder, as "../netconfig" or "a/b" would. The
/// client refuses to load anything else, so the server refuses it first, where a bad row fails loudly.
/// </summary>
public static class AssetNames
{
    /// <summary>The client's limit, in UTF-8 bytes, which is how the client measures a name.</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// True for 1 to 128 UTF-8 bytes that do not start with '.', hold no "..", and hold no '/', '\', ':' or character
    /// below 0x20.
    /// </summary>
    public static bool IsPlain(string? name)
    {
        // UTF-8 never takes fewer bytes than UTF-16 takes chars, so the length alone refuses most long names.
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength || name[0] == '.')
        {
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (c is '/' or '\\' or ':' or < (char)0x20)
            {
                return false;
            }
        }

        return Encoding.UTF8.GetByteCount(name) <= MaxLength;
    }
}
