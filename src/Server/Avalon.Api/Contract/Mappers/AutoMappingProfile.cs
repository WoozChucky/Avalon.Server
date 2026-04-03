using Avalon.Domain.Auth;
using Avalon.Domain.Characters;

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
        Locale = account.Locale,
        Os = account.Os,
        TotalTime = account.TotalTime,
        AccessLevel = account.AccessLevel,
    };

    public static CharacterDto ToDto(this Character character) => new()
    {
        Id = character.Id,
        Name = character.Name,
        Class = character.Class,
        Gender = character.Gender,
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
