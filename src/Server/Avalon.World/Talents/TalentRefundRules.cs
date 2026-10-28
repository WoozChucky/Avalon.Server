using System.Diagnostics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Talents;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Inventory;

namespace Avalon.World.Talents;

/// <summary>
/// Whether a character may refund talent nodes at the Lectern of Paths now (#1181 Part C). It reads and never changes:
/// TalentRefundHandler applies an Ok. The checks, in order: the Lectern's shared prefix
/// (<see cref="DoodadServiceRules.Access" />: dead, stunned, the Lectern not found, beyond its range); the character is
/// casting an ability (an item use or a doodad cast does not count), so no ability leaves mid-cast; the tree's own rules
/// (<see cref="TalentRules.Refund" />: an unknown node, one not held, a refund that would strand a held node); and, when
/// the list is not empty and the price is above 0, the wallet cannot spend it (the wallet already leaves out gold a trade
/// holds). Allocates nothing once the rules' pool is warm. Tick thread only.
/// </summary>
public static class TalentRefundRules
{
    /// <param name="lectern">The Lectern's guid as the request carries it.</param>
    /// <param name="price">The whole refund's gold, every node's price summed.</param>
    /// <param name="wallet">The character's wallet; may be null only when <paramref name="price" /> is 0.</param>
    /// <param name="refusedNode">
    /// The node <see cref="TalentRules.Refund" /> names for UnknownNode, NotAllocated and WouldDisconnect; 0 otherwise.
    /// </param>
    public static TalentResult Decide(CharacterEntity character, IDoodadHost? host, ulong lectern, TalentCatalog tree,
        IReadOnlyList<uint> nodeIds, ulong price, IWallet? wallet, out uint refusedNode)
    {
        if (price > 0 && wallet is null)
            throw new ArgumentNullException(nameof(wallet), "A price above 0 is paid from a wallet.");

        refusedNode = 0;
        DoodadServiceAccess access = DoodadServiceRules.Access(character, host, lectern, DoodadService.Lectern);
        if (access != DoodadServiceAccess.Ok)
        {
            return access switch
            {
                DoodadServiceAccess.Dead => TalentResult.Dead,
                DoodadServiceAccess.Stunned => TalentResult.Stunned,
                DoodadServiceAccess.NotFound => TalentResult.NotFound,
                DoodadServiceAccess.TooFar => TalentResult.TooFar,
                _ => throw new UnreachableException($"DoodadServiceAccess {access} has no talent result"),
            };
        }

        if (character.Spells.IsCasting)
            return TalentResult.Casting;

        TalentResult result = TalentRules.Refund(character.Talents, tree, character.Class, nodeIds, out refusedNode);
        if (result != TalentResult.Ok)
            return result;

        if (nodeIds.Count > 0 && price > 0 && wallet!.CanSpend(price) != WalletResult.Ok)
            return TalentResult.NotEnoughGold;

        return TalentResult.Ok;
    }
}
