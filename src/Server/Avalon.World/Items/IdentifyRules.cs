using System.Diagnostics;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;

namespace Avalon.World.Items;

/// <summary>
/// Whether a character may identify the item in one Bag slot at a Resonance Workbench now (#1179). It reads and never
/// changes: ItemIdentifyHandler applies an Ok. The checks, in order: the Workbench's shared prefix
/// (<see cref="DoodadServiceRules.Access" />: dead, stunned, the Workbench not found, beyond its range); the Bag holds nothing
/// in that slot (a slot past the Bag included); the item is already identified; it is offered in a trade; and, when the
/// cost is above 0, the wallet cannot spend it (the wallet already leaves out gold a trade holds). The wallet is asked
/// for only when the cost is above 0, so a request at no cost needs none (the handler takes the character's only then).
/// Tick thread only.
/// </summary>
public static class IdentifyRules
{
    /// <param name="workbench">The Workbench's guid as the request carries it.</param>
    /// <param name="wallet">The character's wallet; may be null only when <paramref name="cost" /> is 0.</param>
    public static ItemIdentifyResult Decide(CharacterEntity character, IDoodadHost? host, ulong workbench,
        byte bagSlot, ulong cost, IWallet? wallet)
    {
        if (cost > 0 && wallet is null)
            throw new ArgumentNullException(nameof(wallet), "A cost above 0 is paid from a wallet.");

        DoodadServiceAccess access = DoodadServiceRules.Access(character, host, workbench, DoodadService.Workbench);
        if (access != DoodadServiceAccess.Ok)
        {
            return access switch
            {
                DoodadServiceAccess.Dead => ItemIdentifyResult.Dead,
                DoodadServiceAccess.Stunned => ItemIdentifyResult.Stunned,
                DoodadServiceAccess.NotFound => ItemIdentifyResult.NotFound,
                DoodadServiceAccess.TooFar => ItemIdentifyResult.TooFar,
                _ => throw new UnreachableException($"DoodadServiceAccess {access} has no identify result"),
            };
        }

        if (!character.Container(InventoryType.Bag).TryGet(bagSlot, out InventoryItem item))
            return ItemIdentifyResult.NoItem;

        if (item.Identified)
            return ItemIdentifyResult.AlreadyIdentified;

        if (character.Locks.IsLocked(item.InstanceId))
            return ItemIdentifyResult.Locked;

        if (cost > 0 && wallet!.CanSpend(cost) != WalletResult.Ok)
            return ItemIdentifyResult.NotEnoughGold;

        return ItemIdentifyResult.Ok;
    }
}
