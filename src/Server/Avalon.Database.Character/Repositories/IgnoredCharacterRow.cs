using Avalon.Common.ValueObjects;

namespace Avalon.Database.Character.Repositories;

/// <summary>A character on an ignore list, or found by name for one (#723), with the name its row holds now.</summary>
public sealed record IgnoredCharacterRow(CharacterId Id, string Name, DateTime CreatedAt);
