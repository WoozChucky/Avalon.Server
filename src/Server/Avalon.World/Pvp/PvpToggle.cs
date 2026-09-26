using Avalon.Domain.Characters;
using Avalon.Network.Packets.Combat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Options;

namespace Avalon.World.Pvp;

public sealed record PvpStatus(bool Enabled, uint OffInMs);

/// <summary>
/// The one path that changes a PvP flag (#164): CMSG_PVP_TOGGLE and /pvp both call <see cref="Toggle" />,
/// the instance tick calls <see cref="ExpireIfDue" />, and a player-on-player hit calls
/// <see cref="OnPlayerHitPlayer" />. The flag and its timer live on the character row, so every save
/// carries them. The off time is always UTC, because its column is a timestamp with time zone. A dead
/// character may toggle, deliberately: the flag is a choice about the next fight, not an action in this one.
/// Tick thread only. World-side: the modding API cannot reach it.
/// </summary>
public sealed class PvpToggle(IOptions<GameConfiguration> configuration, TimeProvider time)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private TimeSpan Delay => configuration.Value.PvpOffDelay;

    /// <summary>Requests a toggle for the connection's character and replies with its state. A connection with no character gets nothing.</summary>
    public void Toggle(IWorldConnection connection)
    {
        if (connection.Character is not CharacterEntity character)
        {
            return;
        }

        Request(character);
        Send(connection, character);
    }

    /// <summary>Off turns on at once and clears any timer; on with no timer starts it; on with a timer cancels it.</summary>
    public PvpStatus Request(CharacterEntity character)
    {
        Character row = character.Data!;

        if (!row.PvpEnabled)
        {
            row.PvpEnabled = true;
            row.PvpOffAt = null;
        }
        else if (row.PvpOffAt is null)
        {
            row.PvpOffAt = Now + Delay;
        }
        else
        {
            row.PvpOffAt = null;
        }

        character.MarkPvpChanged();
        return StatusOf(character);
    }

    /// <summary>True when the timer was due and the flag has just turned off.</summary>
    public bool ExpireIfDue(CharacterEntity character)
    {
        Character? row = character.Data;
        if (row is not { PvpEnabled: true, PvpOffAt: { } offAt } || offAt > Now)
        {
            return false;
        }

        row.PvpEnabled = false;
        row.PvpOffAt = null;
        character.MarkPvpChanged();
        return true;
    }

    /// <summary>Restarts each player's running timer at the full delay. A flag without a timer is left alone.</summary>
    public void OnPlayerHitPlayer(CharacterEntity attacker, CharacterEntity target)
    {
        foreach (CharacterEntity character in new[] { attacker, target })
        {
            if (character.Data is { PvpEnabled: true, PvpOffAt: not null } row)
            {
                row.PvpOffAt = Now + Delay;
                character.MarkPvpChanged();
            }
        }
    }

    public PvpStatus StatusOf(CharacterEntity character)
    {
        DateTime now = Now;
        uint offInMs = character.PvpOffAt is { } offAt && offAt > now
            ? (uint)Math.Min(uint.MaxValue, Math.Ceiling((offAt - now).TotalMilliseconds))
            : 0u;
        return new PvpStatus(character.PvpEnabled, offInMs);
    }

    public void Send(IWorldConnection connection, CharacterEntity character)
    {
        PvpStatus status = StatusOf(character);
        connection.Send(SPvpStatePacket.Create(status.Enabled, status.OffInMs, connection.CryptoSession.Encrypt));
    }
}
