using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemSlotType : ushort
{
    Head,
    Neck,
    Shoulder,
    Chest,
    Hands,
    Legs,
    Feet,
    Finger,
    Gem,
    MainHand,
    OffHand
}
