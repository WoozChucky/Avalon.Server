using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.State;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Combat;

/// <summary>
/// The combat path's routine lines (#819): one per hit on a character, per death, and per cast refused for its cost,
/// interrupted by movement or dropped because its caster left. They run on the tick many times a second in a busy
/// instance, so they are Debug lines through generated methods that check the level before formatting anything: at
/// the world server's Information default each costs a few nanoseconds and allocates nothing, where a written line
/// costs hundreds (<c>CombatLoggingBenchmarks</c>). Faults stay at Warning and Error where they are.
/// </summary>
internal static partial class CombatLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Name} has been hit by unit {Attacker} for {Damage} damage")]
    public static partial void CharacterHit(ILogger logger, string name, ObjectGuid attacker, uint damage);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Name} has died")]
    public static partial void Died(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "QueueAbility reject {Cost} ability={AbilityId} powerType={PowerType}")]
    public static partial void QueuedCastRefused(ILogger logger, CostCheck cost, AbilityId abilityId, PowerType powerType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "RunInstant reject {Cost} ability={AbilityId} powerType={PowerType}")]
    public static partial void InstantCastRefused(ILogger logger, CostCheck cost, AbilityId abilityId, PowerType powerType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cast interrupted by movement ability={AbilityId} caster={CharId}")]
    public static partial void CastInterruptedByMovement(ILogger logger, AbilityId abilityId, ObjectGuid charId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cast cancelled as its caster left ability={AbilityId} caster={CharId}")]
    public static partial void CastCancelledCasterLeft(ILogger logger, AbilityId abilityId, ObjectGuid charId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped {Count} active scripts as their caster left caster={CharId}")]
    public static partial void ScriptsDroppedCasterLeft(ILogger logger, int count, ObjectGuid charId);
}
