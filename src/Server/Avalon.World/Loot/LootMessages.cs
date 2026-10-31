using System.Diagnostics;
using Avalon.Network.Packets.Loot;
using Avalon.World.Inventory;

namespace Avalon.World.Loot;

/// <summary>
/// Drops described as the loot packet carries them, over messages kept from earlier calls (#890), so describing drops
/// allocates nothing once warm: a message holds numbers only, and its modifier entries are refilled through the pool,
/// which keeps what a smaller drop leaves over (#1179). Its instance keeps one for kills (<c>KillBuffers</c>) and one for
/// the drops coming into each client's view (#1269); tick thread only. <see cref="Clear" /> empties the list it
/// returned, once the packet is encoded.
/// </summary>
internal sealed class LootMessages
{
    private readonly List<LootDropDto> _messages = [];
    private readonly List<LootDropDto> _described = [];
    private readonly ItemModifierDtoPool _modifiers = new();

    /// <summary>
    /// <paramref name="drops" /> as the loot packet carries them, in their order, in a list valid until <see cref="Clear" />;
    /// <paramref name="data" /> gives a drop that did not roll its template's item level.
    /// </summary>
    public List<LootDropDto> Describe(IReadOnlyList<GroundLoot> drops, StaticData data)
    {
        Debug.Assert(_described.Count == 0, "The loot messages to send still hold earlier drops");

        for (int i = 0; i < drops.Count; i++)
        {
            if (i == _messages.Count)
            {
                _messages.Add(new LootDropDto());
            }

            _described.Add(LootDropMapper.Fill(_messages[i], drops[i], data, _modifiers));
        }

        return _described;
    }

    public void Clear() => _described.Clear();
}
