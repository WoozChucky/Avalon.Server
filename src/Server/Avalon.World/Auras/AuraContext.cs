using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// <see cref="IAuraContext" /> over one aura on one unit, for one hook call. Every act goes through the aura system,
/// and so through the combat service, and only while the hook runs: once it has returned the acts do nothing, so a
/// script that keeps its context cannot act later, from anywhere. Nor does it read the aura then: the copy goes back to
/// its aura system's pool once removed and may be another aura later, so the context keeps what it read of it as the
/// hook ended. Tick thread only. World-side.
/// </summary>
public sealed class AuraContext(AuraSystem system, IUnit target, ActiveAura aura, DateTimeOffset now) : IAuraContext
{
    private ActiveAura? _aura = aura;
    private Ended _ended;

    public AuraId AuraId => _aura?.Id ?? _ended.AuraId;
    public string AuraName => _aura?.Template.Name ?? _ended.AuraName;
    public ObjectGuid Target => target.Guid;

    public string TargetName => target switch
    {
        ICharacter character => character.Name,
        ICreature creature => creature.Name,
        _ => string.Empty,
    };

    public uint TargetHealth => target.CurrentHealth;
    public uint TargetMaxHealth => target.Health;
    public bool TargetIsCharacter => target is ICharacter;
    public ObjectGuid Caster => _aura?.CasterGuid ?? _ended.Caster;
    public bool CasterPresent => system.CasterHere(Caster) is not null;
    public uint Stacks => _aura?.Stacks ?? _ended.Stacks;
    public TimeSpan Remaining => _aura?.Schedule.Remaining(now) ?? _ended.Remaining;
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_aura?.DurationMs ?? _ended.DurationMs);

    public void Remove()
    {
        if (_aura is { } live)
            live.ScriptEnded = true;
    }

    public uint Damage(uint amount) => _aura is not { } live || amount == 0 ? 0u : system.ScriptDamage(target, live, amount);

    public uint Heal(uint amount) => _aura is not { } live || amount == 0 ? 0u : system.ScriptHeal(target, live, amount);

    public void Tell(string line)
    {
        if (_aura is not null)
            system.Tell(target, line);
    }

    /// <summary>
    /// The hook it was made for has returned: every act is a no-op from now on, and every read answers what the aura was
    /// as the hook ended, never the copy itself.
    /// </summary>
    internal void End()
    {
        if (_aura is not { } live)
            return;

        _ended = new Ended(live.Id, live.Template.Name, live.CasterGuid, live.Stacks, live.Schedule.Remaining(now),
            live.DurationMs);
        _aura = null;
    }

    private readonly record struct Ended(
        AuraId AuraId, string AuraName, ObjectGuid Caster, uint Stacks, TimeSpan Remaining, uint DurationMs);
}
