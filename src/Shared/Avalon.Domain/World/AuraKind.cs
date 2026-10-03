namespace Avalon.Domain.World;

/// <summary>Who an aura is for: a helpful aura goes on allies, a harmful one on hostile units. 0 is no kind and is refused.</summary>
public enum AuraKind : byte { Helpful = 1, Harmful = 2 }
