using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The cost of the combat path's per-hit log line (#819): one hit through <see cref="CombatService" /> on a living
/// character, whose <c>OnHit</c> writes "{Name} has been hit by unit {Attacker} for {Damage} damage" at Debug, with
/// the line written (a Debug minimum, as every host but the world server runs, and what every hit cost before #819,
/// when the line was Information and the world server ran at Debug) and with the world server's Information minimum;
/// then the line alone, written, and at Debug under an Information minimum through the extension method and through
/// a generated <see cref="LoggerMessage" />.
/// </summary>
/// <remarks>
/// The pipeline is <c>AddCustomLogging</c>'s: Serilog with the same enrichers and output template, behind
/// Microsoft.Extensions.Logging, where <c>AddSerilog</c> lets every level through to Serilog, so Serilog's minimum is
/// the one that counts. The console sink is replaced by one that renders each event with that
/// template into a reused buffer and drops it, so the "written" numbers stop before any console I/O and are a lower
/// bound on what a written line costs the tick.
/// </remarks>
[MemoryDiagnoser]
public partial class CombatLoggingBenchmarks
{
    private const string Template = "[{Timestamp:HH:mm:ss.fff}][{ThreadId}][{Level:u3}]{Message:lj} {NewLine:1}{Exception:1}";
    private const string Line = "{Name} has been hit by unit {Attacker} for {Damage} damage";
    private const uint Health = 1_000_000;

    private readonly List<ServiceProvider> _services = [];
    private CombatService _combat = null!;
    private CharacterEntity _attacker = null!;
    private CharacterEntity _logged = null!;
    private CharacterEntity _quiet = null!;
    private ILogger _debug = null!;
    private ILogger _information = null!;

    [GlobalSetup]
    public void Setup()
    {
        ILoggerFactory debug = Loggers(LogEventLevel.Debug);
        ILoggerFactory information = Loggers(LogEventLevel.Information);

        var config = new CombatConfig();
        _combat = new CombatService(config, new EncounterRegistry(config));
        _attacker = Character(1, debug);
        _logged = Character(2, debug);
        _quiet = Character(3, information);
        _debug = debug.CreateLogger<CombatLoggingBenchmarks>();
        _information = information.CreateLogger<CombatLoggingBenchmarks>();

        _combat.ApplyDamage(_attacker, _logged, 1);
        long rendered = RenderingSink.Rendered;
        _combat.ApplyDamage(_attacker, _quiet, 1);
        LogHit(_information, "Bench2", 1UL, 1u);
        if (_logged.CurrentHealth != Health - 1 || _quiet.CurrentHealth != Health - 1 || rendered == 0 ||
            RenderingSink.Rendered != rendered)
        {
            throw new InvalidOperationException("A hit did not land, or a line was written or filtered unexpectedly.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _services.ForEach(services => services.Dispose());

    private ILoggerFactory Loggers(LogEventLevel minimum)
    {
        Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Is(minimum)
            .Enrich.WithEnvironmentUserName().Enrich.WithMachineName()
            .Enrich.WithProcessId().Enrich.WithProcessName()
            .Enrich.WithThreadId()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionData()
            .WriteTo.Sink(new RenderingSink(Template))
            .CreateLogger();
        ServiceProvider services = new ServiceCollection()
            .AddLogging(builder => builder.ClearProviders().AddSerilog(serilog, dispose: true))
            .BuildServiceProvider();
        _services.Add(services);
        return services.GetRequiredService<ILoggerFactory>();
    }

    private static CharacterEntity Character(uint id, ILoggerFactory loggers)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = $"Bench{id}",
            Class = CharacterClass.Warrior,
            CreationDate = DateTime.UtcNow,
        };
        var entity = new CharacterEntity(loggers, row, new RegenConfiguration()) { Data = row, Guid = new(id) };
        entity.Health = Health;
        entity.CurrentHealth = Health;
        return entity;
    }

    private void Hit(CharacterEntity target)
    {
        _combat.ApplyDamage(_attacker, target, 1);
        if (target.CurrentHealth < Health / 2) target.CurrentHealth = Health;
    }

    /// <summary>A hit whose line is written: every hit on a character before #819.</summary>
    [Benchmark(Baseline = true)]
    public void Hit_line_written() => Hit(_logged);

    /// <summary>The same hit at the world server's Information minimum, where its Debug line is off.</summary>
    [Benchmark]
    public void Hit_world_server_information() => Hit(_quiet);

    [Benchmark]
    public void Line_written() => _debug.LogDebug(Line, "Bench2", 1UL, 1u);

    [Benchmark]
    public void Line_debug_extension_information_minimum() => _information.LogDebug(Line, "Bench2", 1UL, 1u);

    [Benchmark]
    public void Line_debug_generated_information_minimum() => LogHit(_information, "Bench2", 1UL, 1u);

    [LoggerMessage(Level = LogLevel.Debug, Message = Line)]
    private static partial void LogHit(ILogger logger, string name, ulong attacker, uint damage);

    private sealed class RenderingSink(string template) : ILogEventSink
    {
        private readonly MessageTemplateTextFormatter _formatter = new(template);
        private readonly StringWriter _buffer = new();

        public static long Rendered;

        public void Emit(LogEvent logEvent)
        {
            _buffer.GetStringBuilder().Clear();
            _formatter.Format(logEvent, _buffer);
            Rendered++;
        }
    }
}
