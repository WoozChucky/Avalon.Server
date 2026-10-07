using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>An item's cast bar (item use): taking damage never ends it; moving, dying and leaving do.</summary>
public class ItemUseCastsShould
{
    private static readonly ItemTemplateId s_scroll = new(3);

    private readonly RecordingAudience _audience = new();
    private readonly CharacterEntity _character = Inventory.TestCharacters.New(7);
    private readonly List<string> _ends = [];
    private uint _lastId;
    private readonly ItemUseCasts _casts;

    public ItemUseCastsShould() =>
        _casts = new ItemUseCasts(_audience, () => ++_lastId, NullLogger.Instance);

    private void Start(float seconds = 3f) => _casts.Start(new PendingItemUse
    {
        Character = _character,
        Item = s_scroll,
        StartPosition = _character.Position,
        CastId = _casts.TakeCastId(),
        CastTimeSeconds = seconds,
        CanComplete = () => true,
        Completed = () => _ends.Add("completed"),
        Interrupted = () => _ends.Add("interrupted"),
    });

    [Fact]
    public void Broadcast_the_start_and_complete_once_the_time_has_run()
    {
        Start();
        _casts.Update(TimeSpan.FromSeconds(2));
        Assert.Empty(_ends);

        _casts.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(["completed"], _ends);
        Assert.Equal([("start", s_scroll, 1u), ("finish", s_scroll, 1u)], _audience.Sent);
        Assert.False(_casts.IsCasting(_character.Guid));
    }

    [Fact]
    public void Keep_casting_when_the_character_is_hurt()
    {
        _character.Health = 100;
        _character.CurrentHealth = 100;
        Start(1f);
        _character.CurrentHealth = 40;

        _casts.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(["completed"], _ends);
    }

    [Fact]
    public void End_a_cast_by_request_out_loud_and_cancel_one_without_its_callback()
    {
        Start();
        Assert.True(_casts.Interrupt(_character.Guid));
        Assert.False(_casts.Interrupt(_character.Guid));

        Start();
        Assert.True(_casts.Cancel(_character.Guid));

        Assert.Equal(["interrupted"], _ends);
        Assert.Equal(2, _audience.Sent.Count(s => s.Kind == "interrupt"));
    }

    [Fact]
    public void Refuse_a_second_cast_while_one_runs()
    {
        Start();

        Assert.Throws<InvalidOperationException>(() => Start());
    }

    [Fact]
    public void Contain_a_callback_that_throws()
    {
        _casts.Start(new PendingItemUse
        {
            Character = _character,
            Item = s_scroll,
            StartPosition = _character.Position,
            CastId = 9,
            CastTimeSeconds = 0.5f,
            CanComplete = () => true,
            Completed = () => throw new InvalidOperationException("boom"),
            Interrupted = () => { },
        });

        _casts.Update(TimeSpan.FromSeconds(1));

        Assert.False(_casts.IsCasting(_character.Guid));
    }

    /// <summary>
    /// A completion that ends another character's cast and starts a new one for it, on the tick both were due: the old
    /// cast is not completed, and the new one runs on.
    /// </summary>
    [Fact]
    public void Leave_a_cast_started_by_an_earlier_completion_on_the_same_tick_running()
    {
        CharacterEntity other = Inventory.TestCharacters.New(8);
        var otherEnds = new List<string>();
        PendingItemUse OtherCast(float seconds) => new()
        {
            Character = other,
            Item = s_scroll,
            StartPosition = other.Position,
            CastId = _casts.TakeCastId(),
            CastTimeSeconds = seconds,
            CanComplete = () => true,
            Completed = () => otherEnds.Add($"completed {seconds}"),
            Interrupted = () => otherEnds.Add($"interrupted {seconds}"),
        };

        _casts.Start(new PendingItemUse
        {
            Character = _character,
            Item = s_scroll,
            StartPosition = _character.Position,
            CastId = _casts.TakeCastId(),
            CastTimeSeconds = 1f,
            CanComplete = () => true,
            Completed = () =>
            {
                _casts.Interrupt(other.Guid);
                _casts.Start(OtherCast(5f));
            },
            Interrupted = () => { },
        });
        _casts.Start(OtherCast(1f));

        _casts.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(["interrupted 1"], otherEnds);
        Assert.True(_casts.IsCasting(other.Guid));
    }

    /// <summary>A CanComplete that throws is logged at Error naming the character, and interrupts instead.</summary>
    [Fact]
    public void Log_a_throwing_completion_check_naming_the_character_and_interrupt()
    {
        var logger = new RecordingLogger();
        var casts = new ItemUseCasts(_audience, () => ++_lastId, logger);
        casts.Start(new PendingItemUse
        {
            Character = _character,
            Item = s_scroll,
            StartPosition = _character.Position,
            CastId = casts.TakeCastId(),
            CastTimeSeconds = 1f,
            CanComplete = () => throw new InvalidOperationException("boom"),
            Completed = () => _ends.Add("completed"),
            Interrupted = () => _ends.Add("interrupted"),
        });

        casts.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(["interrupted"], _ends);
        (LogLevel level, Exception? error, IReadOnlyList<KeyValuePair<string, object?>> fields) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(fields, f => f.Key == "Character" && Equals(f.Value, _character.Guid));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Error, IReadOnlyList<KeyValuePair<string, object?>> Fields)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception,
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
    }

    private sealed class RecordingAudience : IItemCastAudience
    {
        public List<(string Kind, ItemTemplateId Item, uint CastId)> Sent { get; } = [];

        public void BroadcastItemCastStart(IUnit caster, ItemTemplateId item, float castTimeSeconds, uint castId) =>
            Sent.Add(("start", item, castId));

        public void BroadcastItemCastFinish(IUnit caster, ItemTemplateId item, uint castId) => Sent.Add(("finish", item, castId));

        public void BroadcastItemCastInterrupted(IUnit caster, ItemTemplateId item, uint castId) =>
            Sent.Add(("interrupt", item, castId));
    }
}
