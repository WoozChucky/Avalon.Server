using Avalon.Common.ValueObjects;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Options;

namespace Avalon.World.Persistence;

/// <summary>The periodic save (spec #459, D4), ticked by MapInstance for each character in it.</summary>
public interface ICharacterSaveScheduler
{
    /// <summary>
    /// Tick thread. Counts the character's periodic-save timer down by <paramref name="deltaTime" />
    /// and, when it runs out, saves the character through <see cref="ICharacterSaver" />, which
    /// snapshots synchronously here and queues the write behind any save already in flight for it.
    /// </summary>
    void Tick(IWorldConnection connection, CharacterEntity character, TimeSpan deltaTime);
}

public sealed class CharacterSaveScheduler(ICharacterSaver saver, IOptions<GameConfiguration> configuration)
    : ICharacterSaveScheduler
{
    private readonly TimeSpan _interval = configuration.Value.CharacterSaveInterval;

    public void Tick(IWorldConnection connection, CharacterEntity character, TimeSpan deltaTime)
    {
        if (character.Data is not { } row)
            return;

        TimeSpan remaining = (character.NextPeriodicSaveIn ?? FirstSaveDelay(row.Id, _interval)) - deltaTime;
        if (remaining > TimeSpan.Zero)
        {
            character.NextPeriodicSaveIn = remaining;
            return;
        }

        character.NextPeriodicSaveIn = _interval;

        // Fire and forget: the saver never faults, and a failed save keeps its marks for the next one.
        _ = saver.Save(connection, character);
    }

    /// <summary>
    /// Where inside one interval a character's first save falls. Multiplying the id by 2^32/φ and
    /// keeping the fraction spreads consecutive ids as evenly as any sequence can, so a server full of
    /// characters that entered together saves them over the whole interval, not on one tick.
    /// </summary>
    public static TimeSpan FirstSaveDelay(CharacterId id, TimeSpan interval)
    {
        uint hashed = unchecked(id.Value * 2654435761u);
        return TimeSpan.FromTicks((long)(interval.Ticks * (hashed / 4294967296.0)));
    }
}
