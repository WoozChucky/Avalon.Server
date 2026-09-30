using System.Globalization;
using System.Text;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

/// <summary>A run's random seed from the run's coordinates (FNV-1a), so results never depend on scheduling.</summary>
public static class RunSeed
{
    public static int For(int seed, CharacterClass characterClass, ushort level, string gear, string scenario, int run)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(Key(seed, characterClass, level, gear, scenario, run)))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }

            return (int)(hash ^ (hash >> 32));
        }
    }

    /// <summary>The hashed text, formatted invariantly so a negative seed hashes alike on every machine.</summary>
    private static string Key(int seed, CharacterClass characterClass, ushort level, string gear, string scenario, int run) =>
        string.Create(CultureInfo.InvariantCulture, $"{seed}|{characterClass}|{level}|{gear}|{scenario}|{run}");
}
