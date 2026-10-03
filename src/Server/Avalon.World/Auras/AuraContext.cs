using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// <see cref="IAuraContext" /> over one aura on one unit, for one hook call. Every act goes through the aura system,
/// and so through the combat service, and only while the hook runs: once it has returned the acts do nothing, so a
/// script that keeps its context cannot act later, from anywhere. Tick thread only. World-side.
/// </summary>
public sealed class AuraContext(AuraSystem system, IUnit target, ActiveAura aura, DateTimeOffset now) : IAuraContext
{
    private bool _live = true;

    public AuraId AuraId => aura.Id;
    public string AuraName => aura.Template.Name;
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
    public ObjectGuid Caster => aura.CasterGuid;
    public bool CasterPresent => system.CasterHere(aura.CasterGuid) is not null;
    public uint Stacks => aura.Stacks;
    public TimeSpan Remaining => aura.Schedule.Remaining(now);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(aura.DurationMs);

    public void Remove()
    {
        if (_live)
            aura.ScriptEnded = true;
    }

    public uint Damage(uint amount) => !_live || amount == 0 ? 0u : system.ScriptDamage(target, aura, amount);

    public uint Heal(uint amount) => !_live || amount == 0 ? 0u : system.ScriptHeal(target, aura, amount);

    public void Tell(string line)
    {
        if (_live)
            system.Tell(target, line);
    }

    /// <summary>The hook it was made for has returned: every act is a no-op from now on.</summary>
    internal void End() => _live = false;
}
