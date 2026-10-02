namespace Avalon.Database.Character.Repositories;

/// <summary>What <see cref="ICharacterRepository.TryRenameAsync" /> did (#757).</summary>
public enum CharacterRename
{
    /// <summary>The name and its key were written.</summary>
    Renamed,

    /// <summary>The row is online; nothing was written.</summary>
    Online,

    /// <summary>Another character holds the name's key; nothing was written.</summary>
    NameTaken,

    /// <summary>No character has that id.</summary>
    NotFound,
}
