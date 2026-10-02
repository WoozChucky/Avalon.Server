using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.World.Parties;

/// <summary>Name and class are kept for the roster while the member is offline; level and online state are read live.</summary>
public sealed record PartyMember(CharacterId Id, string Name, CharacterClass Class, DateTimeOffset JoinedAt);
