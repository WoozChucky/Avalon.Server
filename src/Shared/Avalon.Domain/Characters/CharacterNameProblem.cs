namespace Avalon.Domain.Characters;

/// <summary>Why a character name was refused (<see cref="CharacterName.Check(string)" />), or <see cref="None" />.</summary>
public enum CharacterNameProblem
{
    None,
    TooShort,
    TooLong,
    Invalid,
}
