namespace Avalon.Api.Contract;

public sealed class ItemTemplateDto
{
    public ulong Id { get; set; }
    public string Name { get; set; } = "";
    public ItemClass Class { get; set; }
    public ItemSubClass SubClass { get; set; }
    public ItemTemplateFlags Flags { get; set; }
    public bool Stackable { get; set; }
    public uint MaxStackSize { get; set; }
    public uint DisplayId { get; set; }
    public ItemRarity Rarity { get; set; }
    public uint BuyPrice { get; set; }
    public uint SellPrice { get; set; }
    public ItemSlotType? Slot { get; set; }
    public List<CharacterClass> AllowedClasses { get; set; } = [];
    public ushort? ItemPower { get; set; }
    public ushort? RequiredLevel { get; set; }
    public uint? DamageMin1 { get; set; }
    public uint? DamageMax1 { get; set; }
    public DamageType? DamageType1 { get; set; }
    public uint? DamageMin2 { get; set; }
    public uint? DamageMax2 { get; set; }
    public DamageType? DamageType2 { get; set; }
    public StatType? StatType1 { get; set; }
    public uint? StatValue1 { get; set; }
    public StatType? StatType2 { get; set; }
    public uint? StatValue2 { get; set; }
    public StatType? StatType3 { get; set; }
    public uint? StatValue3 { get; set; }
    public StatType? StatType4 { get; set; }
    public uint? StatValue4 { get; set; }
    public StatType? StatType5 { get; set; }
    public uint? StatValue5 { get; set; }
    public StatType? StatType6 { get; set; }
    public uint? StatValue6 { get; set; }
    public StatType? StatType7 { get; set; }
    public uint? StatValue7 { get; set; }
    public StatType? StatType8 { get; set; }
    public uint? StatValue8 { get; set; }
    public StatType? StatType9 { get; set; }
    public uint? StatValue9 { get; set; }
    public StatType? StatType10 { get; set; }
    public uint? StatValue10 { get; set; }
}
