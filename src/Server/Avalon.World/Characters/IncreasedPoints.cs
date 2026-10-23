using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalon.Combat;
using Avalon.Network.Packets.Combat;

namespace Avalon.World.Characters;

/// <summary>
/// The increased percentage points of each <see cref="OffenceStat" /> (#1178), the part of the offence block the character
/// sheet shows: what <see cref="CharacterSheet" /> keeps of it, so a change the sheet does not show (a more, which only
/// each ability's amount carries) never resends it. Inline, so it is a value that never allocates; its equality is
/// written by hand because the sheet flush compares it.
/// </summary>
public struct IncreasedPoints : IEquatable<IncreasedPoints>
{
    private Values _values;

    /// <summary>The increased points of every key of <paramref name="offence" />.</summary>
    public static IncreasedPoints Of(in OffenceStats offence)
    {
        IncreasedPoints points = default;
        for (int i = 0; i < OffenceStats.Count; i++)
            points._values[i] = offence.Increased((OffenceStat)i);

        return points;
    }

    /// <summary>The points of the key numbered <paramref name="index" />.</summary>
    public readonly float this[int index] => _values[index];

    /// <summary>Writes every key's points into <paramref name="destination" />, in key order.</summary>
    public readonly void CopyTo(Span<float> destination) => ((ReadOnlySpan<float>)_values).CopyTo(destination);

    public readonly bool Equals(IncreasedPoints other)
    {
        for (int i = 0; i < OffenceStats.Count; i++)
        {
            if (!_values[i].Equals(other._values[i])) return false;
        }

        return true;
    }

    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is IncreasedPoints other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        for (int i = 0; i < OffenceStats.Count; i++)
            hash.Add(_values[i]);

        return hash.ToHashCode();
    }

    public static bool operator ==(IncreasedPoints left, IncreasedPoints right) => left.Equals(right);

    public static bool operator !=(IncreasedPoints left, IncreasedPoints right) => !left.Equals(right);

    [InlineArray(OffenceStats.Count)]
    private struct Values
    {
        private float _element0;
    }
}
