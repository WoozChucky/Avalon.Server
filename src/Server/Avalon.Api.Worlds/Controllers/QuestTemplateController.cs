using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Hosting.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Worlds.Controllers;

/// <summary>
/// One world's quests (#714), read-only: quests stay data and migrations. Game masters only, since a quest's
/// stages, drops and script are the storyline ahead of a player.
/// </summary>
[ApiController]
[Authorize(Policy = AvalonRoles.GameMaster)]
[WorldScoped]
[Route("world/{worldId:int}/quest-template")]
public class QuestTemplateController : BaseController
{
    private readonly IQuestRepository _quests;
    private readonly ILocalizedTextRepository _texts;

    public QuestTemplateController(IQuestRepository quests, ILocalizedTextRepository texts)
    {
        _quests = quests;
        _texts = texts;
    }

    [HttpGet(Name = "ListQuestTemplates")]
    [ProducesResponseType(typeof(PagedResult<QuestTemplateDto>), StatusCodes.Status200OK)]
    public async Task<PagedResult<QuestTemplateDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 50 ? 50 : pageSize;

        PagedResult<QuestTemplate> result = await _quests.PaginateAsync(page, pageSize, ct);
        IReadOnlyDictionary<int, string> texts = await TextsAsync(result.Items.SelectMany(QuestMapping.TextIdsOf), ct);
        return result.MapTo(q => q.ToDto(texts));
    }

    [HttpGet("{id:long}", Name = "GetQuestTemplateById")]
    [ProducesResponseType(typeof(QuestTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] uint id, CancellationToken ct)
    {
        QuestTemplate? quest = await _quests.FindByIdAsync(new QuestTemplateId(id), ct);
        if (quest is null) return NotFound();

        IReadOnlyDictionary<int, string> texts = await TextsAsync(QuestMapping.TextIdsOf(quest), ct);
        return Ok(quest.ToDto(texts));
    }

    private async Task<IReadOnlyDictionary<int, string>> TextsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        IReadOnlyCollection<LocalizedText> rows =
            await _texts.GetByIdsAsync(ids.Distinct().Select(i => new LocalizedTextId(i)), ct);
        return rows.ToDictionary(t => t.Id.Value, t => t.Text);
    }
}
