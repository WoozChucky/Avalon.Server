namespace Avalon.Api.Contract;

/// <summary>A Collect objective's item drops from this creature at this chance, a percentage.</summary>
public sealed class QuestItemDropDto
{
    public uint ObjectiveId { get; set; }
    public ulong CreatureTemplateId { get; set; }
    public float Chance { get; set; }
}
