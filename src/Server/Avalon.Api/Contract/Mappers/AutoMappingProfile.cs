using AutoMapper;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;

namespace Avalon.Api.Contract.Mappers;

public class AutoMappingProfile : Profile
{
    public AutoMappingProfile()
    {
        CreateMap<Account, AccountDto>();
        CreateMap<Character, CharacterDto>();
    }
}
