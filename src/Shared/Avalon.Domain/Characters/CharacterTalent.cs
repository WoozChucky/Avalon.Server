using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>
/// One talent node a character holds (#1181): a row per allocated node, the pair the key. <see cref="NodeId" /> names a
/// World DB <c>TalentNodes</c> row, with no foreign key across the two databases; a row naming a node the tree no longer
/// holds is dropped at select. Deleting the character deletes its rows.
/// </summary>
public class CharacterTalent
{
    public CharacterId CharacterId { get; set; } = default!;
    public int NodeId { get; set; }
}
