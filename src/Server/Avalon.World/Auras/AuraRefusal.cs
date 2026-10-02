using Avalon.Common.ValueObjects;

namespace Avalon.World.Auras;

/// <summary>An aura row left out of the catalog, and why.</summary>
public sealed record AuraRefusal(AuraId Id, string Name, string Reason)
{
    public override string ToString() => $"aura {Id.Value} '{Name}': {Reason}";
}
