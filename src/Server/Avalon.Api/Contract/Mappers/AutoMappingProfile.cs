using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.Api.Contract.Mappers;

public static class MappingExtensions
{
    public static AccountDto ToDto(this Account account) => new()
    {
        Id = account.Id,
        Username = account.Username,
        Email = account.Email,
        JoinDate = account.JoinDate,
        LastIp = account.LastIp,
        Locked = account.Locked,
        MuteTime = account.MuteTime,
        MuteReason = account.MuteReason,
        Online = account.Online,
        Locale = (Avalon.Api.Contract.AccountLocale)account.Locale,
        Os = (Avalon.Api.Contract.OperatingSystem)account.Os,
        TotalTime = account.TotalTime,
        AccessLevel = (Avalon.Api.Contract.AccountAccessLevel)account.AccessLevel,
    };

    /// <summary>A character with the world it lives on (#523).</summary>
    public static CharacterDto ToDto(this Character character, ushort worldId, string worldName) => new()
    {
        Id = character.Id,
        WorldId = worldId,
        WorldName = worldName,
        Name = character.Name,
        Class = character.Class,
        Gender = (CharacterGender) character.Gender,
        Level = character.Level,
        Experience = character.Experience,
        Map = character.Map,
        Online = character.Online,
        TotalTime = character.TotalTime,
        TotalKills = character.TotalKills,
        ChosenTitle = character.ChosenTitle,
        Health = character.Health,
        Latency = character.Latency,
        CreationDate = character.CreationDate,
        DeleteDate = character.DeleteDate,
    };
}
