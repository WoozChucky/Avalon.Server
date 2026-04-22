namespace Avalon.Api.Contract;

[Flags]
public enum ItemTemplateFlags
{
    None = 0,
    Attunned = 1,
    AccountAttunned = 2,
    Unique = 4,
    UniqueEquipped = 8,
    AttuneOnPickup = 16,
    AttuneOnEquip = 32,
    AttuneOnUse = 64,
    AttuneOnAccount = 128,
    NoSell = 256,
    NoDestroy = 512,
    NoTrade = 1024,
}
