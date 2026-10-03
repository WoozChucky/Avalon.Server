namespace Avalon.World.Auras;

/// <summary>What holds an instance's aura system: MapInstance. World-side, not on IMapInstance (the modding API).</summary>
public interface IAuraHost
{
    AuraSystem Auras { get; }
}
