using System.Diagnostics;
using Avalon.Common;
using Avalon.Network.Packets.Loot;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;

namespace Avalon.World.Instances;

/// <summary>
/// The lists one kill fills and reads before it returns (#890), kept by its <see cref="MapInstance" /> so that a kill
/// allocates none of them. An instance's kills run one at a time on its tick, so one set serves them all; it belongs to
/// the instance and never to a static, since instances may come to tick on separate threads (#639).
/// <see cref="Clear" /> empties it when the kill is done, so it keeps no character or drop alive between kills.
/// </summary>
internal sealed class KillBuffers
{
    // The loot messages this instance's kills have filled so far, refilled by the next: a message holds numbers only,
    // and its modifier entries are refilled through the pool, which keeps what a smaller drop leaves over (#1179).
    private readonly List<LootDropDto> _messages = [];
    private readonly ItemModifierDtoPool _modifiers = new();

    /// <summary>
    /// Who shares the kill, in the party's join order. The kill hands it to the quest service and the loot allocator,
    /// which read it during the call: nothing may keep it, since the next kill fills it again.
    /// </summary>
    public List<ICharacter> Eligible { get; } = [];

    /// <summary>The key each party member is looked up by among the instance's characters; never stored anywhere.</summary>
    public ObjectGuid MemberKey { get; } = new();

    /// <summary>Each counted member's share of the kill's experience, in the order of <see cref="Eligible" />.</summary>
    public List<ExperienceShare> Shares { get; } = [];

    /// <summary>The quest drops the kill rolled, each with the character it was rolled for (#883).</summary>
    public List<(RolledDrop Drop, uint Owner)> QuestDrops { get; } = [];

    /// <summary>
    /// What the kill rolled: the table's drops, which the loot roller adds here and the item roller turns into their copies
    /// (#1179), then the quest drops.
    /// </summary>
    public List<RolledDrop> Rolled { get; } = [];

    /// <summary>The drops placed around the corpse, before they go into the instance's store.</summary>
    public List<GroundLoot> Placed { get; } = [];

    /// <summary>The drops as the loot packet carries them, which is encoded before the kill returns.</summary>
    public List<LootDropDto> Sent { get; } = [];

    /// <summary>
    /// Fills <see cref="Sent" /> from <see cref="Placed" />, over messages kept from earlier kills; <paramref name="data" />
    /// gives a drop that did not roll its template's item level.
    /// </summary>
    public List<LootDropDto> DescribePlaced(StaticData data)
    {
        Debug.Assert(Sent.Count == 0, "The loot messages to send still hold an earlier kill's");

        for (int i = 0; i < Placed.Count; i++)
        {
            if (i == _messages.Count)
            {
                _messages.Add(new LootDropDto());
            }

            Sent.Add(LootDropMapper.Fill(_messages[i], Placed[i], data, _modifiers));
        }

        return Sent;
    }

    public void Clear()
    {
        Eligible.Clear();
        Shares.Clear();
        QuestDrops.Clear();
        Rolled.Clear();
        Placed.Clear();
        Sent.Clear();
    }
}
