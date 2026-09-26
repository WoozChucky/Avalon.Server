using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Pvp;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Server.World.UnitTests.Pvp;

public class PvpToggleShould
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly PvpToggle _toggle;

    public PvpToggleShould() =>
        _toggle = new PvpToggle(Options.Create(new GameConfiguration { PvpOffDelay = TimeSpan.FromMinutes(5) }), _clock);

    [Fact]
    public void Turn_on_at_once()
    {
        CharacterEntity character = TestCharacters.New(1);

        PvpStatus status = _toggle.Request(character);

        Assert.Equal(new PvpStatus(true, 0), status);
        Assert.True(character.PvpEnabled);
        Assert.Null(character.PvpOffAt);
        Assert.True(character.SaveState.PvpDirty);
    }

    [Fact]
    public void Start_the_off_timer_and_stay_hostile_until_it_runs_out()
    {
        CharacterEntity character = TestCharacters.New(1);
        _toggle.Request(character);

        PvpStatus status = _toggle.Request(character);

        Assert.Equal(new PvpStatus(true, 300_000), status);
        Assert.True(character.PvpEnabled);
        Assert.Equal(Now.UtcDateTime.AddMinutes(5), character.PvpOffAt);
    }

    /// <summary>PvpOffAt is a timestamp with time zone column, and Npgsql refuses a DateTime that is not UTC.</summary>
    [Fact]
    public void Write_the_off_time_as_utc()
    {
        CharacterEntity character = TestCharacters.New(1);
        _toggle.Request(character);

        _toggle.Request(character);

        Assert.Equal(DateTimeKind.Utc, character.PvpOffAt!.Value.Kind);
    }

    [Fact]
    public void Write_the_restarted_off_time_as_utc()
    {
        CharacterEntity attacker = TestCharacters.New(1);
        CharacterEntity target = TestCharacters.New(2);
        _toggle.Request(attacker);
        _toggle.Request(attacker);

        _toggle.OnPlayerHitPlayer(attacker, target);

        Assert.Equal(DateTimeKind.Utc, attacker.PvpOffAt!.Value.Kind);
    }

    [Fact]
    public void Cancel_a_running_timer_and_stay_on()
    {
        CharacterEntity character = TestCharacters.New(1);
        _toggle.Request(character);
        _toggle.Request(character);

        PvpStatus status = _toggle.Request(character);

        Assert.Equal(new PvpStatus(true, 0), status);
        Assert.Null(character.PvpOffAt);
    }

    [Fact]
    public void Turn_off_once_the_timer_is_due_and_not_before()
    {
        CharacterEntity character = TestCharacters.New(1);
        _toggle.Request(character);
        _toggle.Request(character);

        _clock.Now = Now.AddMinutes(5).AddMilliseconds(-1);
        Assert.False(_toggle.ExpireIfDue(character));

        _clock.Now = Now.AddMinutes(5);
        Assert.True(_toggle.ExpireIfDue(character));
        Assert.False(character.PvpEnabled);
        Assert.Null(character.PvpOffAt);
    }

    [Fact]
    public void Never_expire_a_flag_without_a_timer_or_a_flag_that_is_off()
    {
        CharacterEntity on = TestCharacters.New(1);
        CharacterEntity off = TestCharacters.New(2);
        _toggle.Request(on);
        _clock.Now = Now.AddYears(1);

        Assert.False(_toggle.ExpireIfDue(on));
        Assert.False(_toggle.ExpireIfDue(off));
        Assert.True(on.PvpEnabled);
        Assert.False(off.PvpEnabled);
    }

    [Fact]
    public void Restart_both_players_running_timers_on_a_pvp_hit()
    {
        CharacterEntity attacker = TestCharacters.New(1);
        CharacterEntity target = TestCharacters.New(2);
        foreach (CharacterEntity c in new[] { attacker, target })
        {
            _toggle.Request(c);
            _toggle.Request(c);
        }

        _clock.Now = Now.AddMinutes(4);
        _toggle.OnPlayerHitPlayer(attacker, target);

        Assert.Equal(Now.UtcDateTime.AddMinutes(9), attacker.PvpOffAt);
        Assert.Equal(Now.UtcDateTime.AddMinutes(9), target.PvpOffAt);
    }

    [Fact]
    public void Leave_a_flag_without_a_timer_alone_on_a_pvp_hit()
    {
        CharacterEntity attacker = TestCharacters.New(1);
        CharacterEntity target = TestCharacters.New(2);
        _toggle.Request(attacker);
        _toggle.Request(target);

        _toggle.OnPlayerHitPlayer(attacker, target);

        Assert.Null(attacker.PvpOffAt);
        Assert.Null(target.PvpOffAt);
    }

    [Fact]
    public void Round_the_time_left_up_to_the_millisecond()
    {
        CharacterEntity character = TestCharacters.New(1);
        _toggle.Request(character);
        _toggle.Request(character);
        _clock.Now = Now.AddMinutes(5).AddTicks(-1);

        Assert.Equal(1u, _toggle.StatusOf(character).OffInMs);
    }
}
