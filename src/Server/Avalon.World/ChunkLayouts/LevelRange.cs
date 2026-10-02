using System.Runtime.InteropServices;

namespace Avalon.World.ChunkLayouts;

/// <summary>An inclusive range of creature levels.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LevelRange(ushort Min, ushort Max);
