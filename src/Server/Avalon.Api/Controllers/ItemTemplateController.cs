using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[Route("item-template")]
public class ItemTemplateController : BaseController
{
    private readonly IItemTemplateRepository _repository;

    public ItemTemplateController(IItemTemplateRepository repository)
    {
        _repository = repository;
    }

    [HttpGet(Name = "ListItemTemplates")]
    [ProducesResponseType(typeof(PagedResult<ItemTemplateDto>), StatusCodes.Status200OK)]
    public async Task<PagedResult<ItemTemplateDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filter = new ItemTemplatePaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
        };

        var result = await _repository.PaginateAsync(filter, track: false, ct);
        return result.MapTo(ToDto);
    }

    [HttpGet("{id:long}", Name = "GetItemTemplateById")]
    [ProducesResponseType(typeof(ItemTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] ulong id, CancellationToken ct)
    {
        var template = await _repository.FindByIdAsync(new ItemTemplateId(id), track: false, ct);
        return template is null ? NotFound() : Ok(ToDto(template));
    }

    private static ItemTemplateDto ToDto(ItemTemplate t) => new()
    {
        Id = t.Id.Value,
        Name = t.Name,
        Class = (Avalon.Api.Contract.ItemClass)t.Class,
        SubClass = (Avalon.Api.Contract.ItemSubClass)t.SubClass,
        Flags = (Avalon.Api.Contract.ItemTemplateFlags)t.Flags,
        Stackable = t.Stackable,
        MaxStackSize = t.MaxStackSize,
        DisplayId = t.DisplayId,
        Rarity = (Avalon.Api.Contract.ItemRarity)t.Rarity,
        BuyPrice = t.BuyPrice,
        SellPrice = t.SellPrice,
        Slot = (Avalon.Api.Contract.ItemSlotType?)t.Slot,
        AllowedClasses = t.AllowedClasses is null ? [] : t.AllowedClasses.Select(c => (Avalon.Api.Contract.CharacterClass)c).ToList(),
        ItemPower = t.ItemPower,
        RequiredLevel = t.RequiredLevel,
        DamageMin1 = t.DamageMin1,
        DamageMax1 = t.DamageMax1,
        DamageType1 = (Avalon.Api.Contract.DamageType?)t.DamageType1,
        DamageMin2 = t.DamageMin2,
        DamageMax2 = t.DamageMax2,
        DamageType2 = (Avalon.Api.Contract.DamageType?)t.DamageType2,
        StatType1 = (Avalon.Api.Contract.StatType?)t.StatType1,
        StatValue1 = t.StatValue1,
        StatType2 = (Avalon.Api.Contract.StatType?)t.StatType2,
        StatValue2 = t.StatValue2,
        StatType3 = (Avalon.Api.Contract.StatType?)t.StatType3,
        StatValue3 = t.StatValue3,
        StatType4 = (Avalon.Api.Contract.StatType?)t.StatType4,
        StatValue4 = t.StatValue4,
        StatType5 = (Avalon.Api.Contract.StatType?)t.StatType5,
        StatValue5 = t.StatValue5,
        StatType6 = (Avalon.Api.Contract.StatType?)t.StatType6,
        StatValue6 = t.StatValue6,
        StatType7 = (Avalon.Api.Contract.StatType?)t.StatType7,
        StatValue7 = t.StatValue7,
        StatType8 = (Avalon.Api.Contract.StatType?)t.StatType8,
        StatValue8 = t.StatValue8,
        StatType9 = (Avalon.Api.Contract.StatType?)t.StatType9,
        StatValue9 = t.StatValue9,
        StatType10 = (Avalon.Api.Contract.StatType?)t.StatType10,
        StatValue10 = t.StatValue10,
    };
}
