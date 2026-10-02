using Avalon.Common.ValueObjects;

namespace Avalon.Database.Character.Repositories;

/// <summary>A character of this world found by name (#723).</summary>
public sealed record CharacterNameMatch(CharacterId Id, string Name);
