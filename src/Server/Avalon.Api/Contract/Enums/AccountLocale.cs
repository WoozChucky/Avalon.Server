using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountLocale : ushort
{
    enUS,
    enGB,
    deDE,
    esES,
    esMX,
    frFR,
    itIT,
    plPL,
    ptBR,
    ptPT,
    ruRU,
    koKR,
    zhCN,
    zhTW,
    jaJP,
    thTH,
    viVN,
    idID,
    msMY
}
