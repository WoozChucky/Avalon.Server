using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CAST_ABILITY)]
public class CastAbilityHandler(ILogger<CastAbilityHandler> logger, IWorld world, CombatConfig combatConfig)
    : WorldPacketHandler<CCastAbilityPacket>
{
    public override void Execute(IWorldConnection connection, CCastAbilityPacket packet)
    {
        var attacker = connection.Character;
        if (attacker is null || attacker.IsDead)
        {
            logger.LogDebug("Dropped CMSG_CAST_ABILITY from dead/missing char");
            return;
        }

        // GCD check uses packet.AbilityId directly — no ability resolution required.
        // Logically: if the player is still inside the global cooldown window, reject before
        // we even bother validating ability ownership.
        double sinceLastCast = (DateTime.UtcNow - attacker.LastCastStartTime).TotalMilliseconds;
        if (sinceLastCast < combatConfig.GcdMs)
        {
            uint remaining = (uint)(combatConfig.GcdMs - sinceLastCast);
            connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, remaining,
                connection.CryptoSession.Encrypt));
            return;
        }

        IAbility? ability = attacker.Spells[packet.AbilityId];
        if (ability is null)
        {
            logger.LogDebug("Ability {AbilityId} not owned", packet.AbilityId);
            return;
        }

        if (ability.CooldownTimer > 0)
        {
            connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, ability.CooldownTimer,
                connection.CryptoSession.Encrypt));
            return;
        }
        // E5..E6 fill the rest.
    }
}
