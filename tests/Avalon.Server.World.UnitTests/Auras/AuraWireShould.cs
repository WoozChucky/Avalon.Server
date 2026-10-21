using System.Reflection;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Auras;
using Avalon.World.Auras;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>#980: the aura lists' and updates' entries are reused from one packet to the next.</summary>
public class AuraWireShould
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// An entry one packet left with every member set (an update's action, say) says only what a new one would when the
    /// next packet, a list or an update, is written from it; a member a writer does not set fails here. Each entry
    /// carries its aura's crowd control (#770), a slow's derived bit included, in both.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Write_reused_entries_as_new_ones(bool listAfterUpdate)
    {
        var auras = new UnitAuras();
        auras.Add(Aura(AuraTestData.Bleed(), new ObjectGuid(ObjectType.Character, 7)), s_t0);
        auras.Add(Aura(AuraTestData.Burn(), new ObjectGuid(ObjectType.Character, 8)), s_t0.AddSeconds(1));
        auras.Add(Aura(AuraTestData.Crippled(), new ObjectGuid(ObjectType.Character, 8)), s_t0.AddSeconds(1));
        auras.Add(AuraTestData.HeldControl(CrowdControl.Root | CrowdControl.Silence), s_t0.AddSeconds(1));
        uint[] control = [0u, 0u, 8u, 2u | 4u];
        Assert.Equal(control, new AuraWire().List(auras, s_t0).Select(e => e.Control));
        Assert.Equal(control, new AuraWire().Updates(auras.Changes).Select(e => e.Control));
        var reused = new AuraWire();
        foreach (AuraEntryDto entry in listAfterUpdate ? reused.Updates(auras.Changes) : reused.List(auras, s_t0))
            FillEveryMember(entry);

        byte[] written = Encode(listAfterUpdate ? reused.List(auras, s_t0) : reused.Updates(auras.Changes));

        var fresh = new AuraWire();
        Assert.Equal(Encode(listAfterUpdate ? fresh.List(auras, s_t0) : fresh.Updates(auras.Changes)), written);
    }

    private static ActiveAura Aura(AuraTemplate template, ObjectGuid caster) =>
        new(template, caster, AuraSource.None, 1, new AuraSnapshot(5f, 0f, 1),
            AuraSchedule.Start(s_t0, template.DurationMs, template.TickIntervalMs), template.DurationMs, s_t0.UtcDateTime);

    private static void FillEveryMember(AuraEntryDto entry)
    {
        foreach (PropertyInfo member in typeof(AuraEntryDto).GetProperties())
        {
            object value = member.PropertyType.IsEnum
                ? Enum.GetValues(member.PropertyType).GetValue(1)!
                : Convert.ChangeType(77, member.PropertyType);
            member.SetValue(entry, value);
        }
    }

    private static byte[] Encode(List<AuraEntryDto> entries)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new SAuraUpdatePacket { UnitGuid = 1, Entries = entries });
        return stream.ToArray();
    }
}
