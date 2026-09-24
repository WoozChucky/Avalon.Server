using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;

namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// The AI for a creature that is not a monster. It stands where it was placed, never aggros, never
/// chases and never fights. Town NPCs run this.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two constructor arguments, and no more.</b> <c>CreaturePlacementService.AttachScript</c> builds
/// every AI script with <c>ActivatorUtilities.CreateInstance(sp, scriptType, creature, instance)</c>,
/// which supplies exactly the creature and the instance — anything else has to come from DI. The two
/// existing passive scripts both break that contract: <c>CreatureIdleScript</c> wants a
/// <c>float idleTime</c> and <c>CreaturePatrolScript</c> wants a <c>Vector3[] waypoints</c>, so
/// neither can be constructed from a <c>ScriptName</c> string in seed data. The resulting throw is
/// swallowed by AttachScript's catch, which means the creature silently ends up with no script.
/// </para>
/// <para>
/// This script is not what makes a town NPC unkillable — <see cref="ICreature.Invulnerable"/> is,
/// enforced in <c>CombatService.ApplyDamageCore</c>. Not implementing <c>OnHit</c> is the second
/// layer: health is only ever subtracted inside <c>CreatureCombatScript.OnHit</c>, so a creature
/// running this script has no code path that can reduce its health.
/// </para>
/// <para>
/// Interaction — talking to an NPC — does not exist yet and belongs here when it does. See #431.
/// </para>
/// </remarks>
public class TownNpcScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
{
    public override object State { get; set; } = false;

    /// <summary>
    /// Always false. <c>MapInstance</c> calls <c>Script.Update</c> unconditionally, so this governs
    /// chained scripts only — of which a town NPC has none. Both are no-ops, which is the point.
    /// </summary>
    protected override bool ShouldRun() => false;
}
