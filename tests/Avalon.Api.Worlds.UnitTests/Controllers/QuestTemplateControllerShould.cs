using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Worlds.Controllers;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Controllers;

/// <summary>#714: one world's quests, read-only, for game masters, every field mapped and every text resolved.</summary>
public class QuestTemplateControllerShould
{
    private readonly IQuestRepository _quests = Substitute.For<IQuestRepository>();
    private readonly ILocalizedTextRepository _texts = Substitute.For<ILocalizedTextRepository>();

    public QuestTemplateControllerShould()
    {
        // Every text reads "text <id>", except 99, which has no row.
        _texts.GetByIdsAsync(Arg.Any<IEnumerable<LocalizedTextId>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<LocalizedTextId>>()
                .Where(id => id.Value != 99)
                .Select(id => new LocalizedText { Id = id, Text = "text " + id.Value })
                .ToList());
    }

    private QuestTemplateController MakeSut() =>
        new(_quests, _texts) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static QuestTemplate Quest() => new()
    {
        Id = 3,
        TitleTextId = 33,
        DescriptionTextId = 34,
        CompletionTextId = 35,
        GiverCreatureId = 2,
        EnderCreatureId = 11,
        LevelRequirement = 2,
        ClassRequirement = CharacterClass.Hunter,
        RequiredQuestId = 2,
        ScriptName = "AlphaHowlScript",
        RewardExperience = 600,
        RewardMoney = 400,
        Stages =
        [
            new QuestStage { QuestId = 3, Sequence = 1, DescriptionTextId = 41 },
            new QuestStage { QuestId = 3, Sequence = 0 },
        ],
        Objectives =
        [
            new QuestObjective
            {
                Id = 303, QuestId = 3, StageSequence = 1, Type = Avalon.Domain.World.QuestObjectiveType.Talk,
                CreatureTemplateId = 11, Count = 1, DescriptionTextId = 38,
            },
            new QuestObjective
            {
                Id = 302, QuestId = 3, StageSequence = 0, Type = Avalon.Domain.World.QuestObjectiveType.Collect,
                ItemTemplateId = 57, Count = 4, DescriptionTextId = 99,
                Drops =
                [
                    new QuestItemDrop { ObjectiveId = 302, CreatureTemplateId = 7, Chance = 25f },
                    new QuestItemDrop { ObjectiveId = 302, CreatureTemplateId = 4, Chance = 60f },
                ],
            },
            new QuestObjective
            {
                Id = 301, QuestId = 3, StageSequence = 0, Type = Avalon.Domain.World.QuestObjectiveType.Kill,
                CreatureTemplateId = 5, Count = 3, DescriptionTextId = 36,
            },
            new QuestObjective
            {
                Id = 304, QuestId = 3, StageSequence = 1, Type = Avalon.Domain.World.QuestObjectiveType.Scripted,
                Count = 2, DescriptionTextId = 39,
            },
        ],
        ItemRewards =
        [
            new QuestItemReward { QuestId = 3, ItemTemplateId = 58, Count = 1 },
            new QuestItemReward { QuestId = 3, ItemTemplateId = 56, Count = 2 },
        ],
    };

    private async Task<QuestTemplateDto> GetOk(uint id) =>
        Assert.IsType<QuestTemplateDto>(Assert.IsType<OkObjectResult>(await MakeSut().Get(id, CancellationToken.None)).Value);

    [Fact]
    public async Task Map_every_field_with_its_texts_resolved()
    {
        _quests.FindByIdAsync(new QuestTemplateId(3), Arg.Any<CancellationToken>()).Returns(Quest());

        QuestTemplateDto dto = await GetOk(3);

        Assert.Equal(3u, dto.Id);
        Assert.Equal(("text 33", 33), (dto.Title, dto.TitleTextId));
        Assert.Equal(("text 34", 34), (dto.Description, dto.DescriptionTextId));
        Assert.Equal(("text 35", 35), (dto.CompletionText, dto.CompletionTextId));
        Assert.Equal((2ul, 11ul), (dto.GiverCreatureTemplateId, dto.EnderCreatureTemplateId));
        Assert.Equal((ushort)2, dto.LevelRequirement);
        Assert.Equal(CharacterClass.Hunter, dto.ClassRequirement);
        Assert.Equal(2u, dto.RequiredQuestId);
        Assert.Equal("AlphaHowlScript", dto.ScriptName);
        Assert.Equal((600u, 400ul), (dto.RewardExperience, dto.RewardMoney));

        Assert.Equal([0, 1], dto.Stages.Select(s => s.Sequence));
        Assert.Equal(((string?)null, (int?)null), (dto.Stages[0].Text, dto.Stages[0].TextId));
        Assert.Equal(("text 41", (int?)41), (dto.Stages[1].Text, dto.Stages[1].TextId));

        Assert.Equal([301u, 302u], dto.Stages[0].Objectives.Select(o => o.Id));
        QuestObjectiveTemplateDto kill = dto.Stages[0].Objectives[0];
        Assert.Equal((Avalon.Api.Contract.QuestObjectiveType.Kill, (ulong?)5, (ulong?)null, 3u, "text 36", 36),
            (kill.Type, kill.CreatureTemplateId, kill.ItemTemplateId, kill.Count, kill.Text, kill.TextId));
        QuestObjectiveTemplateDto collect = dto.Stages[0].Objectives[1];
        Assert.Equal((Avalon.Api.Contract.QuestObjectiveType.Collect, (ulong?)null, (ulong?)57, 4u, "", 99),
            (collect.Type, collect.CreatureTemplateId, collect.ItemTemplateId, collect.Count, collect.Text, collect.TextId));
        Assert.Equal([303u, 304u], dto.Stages[1].Objectives.Select(o => o.Id));
        QuestObjectiveTemplateDto talk = dto.Stages[1].Objectives[0];
        Assert.Equal((Avalon.Api.Contract.QuestObjectiveType.Talk, (ulong?)11, "text 38"), (talk.Type, talk.CreatureTemplateId, talk.Text));
        QuestObjectiveTemplateDto scripted = dto.Stages[1].Objectives[1];
        Assert.Equal((Avalon.Api.Contract.QuestObjectiveType.Scripted, (ulong?)null, (ulong?)null, 2u),
            (scripted.Type, scripted.CreatureTemplateId, scripted.ItemTemplateId, scripted.Count));

        Assert.Equal([(56ul, 2u), (58ul, 1u)], dto.ItemRewards.Select(r => (r.ItemTemplateId, r.Count)));
        Assert.Equal([(302u, 4ul, 60f), (302u, 7ul, 25f)],
            dto.ItemDrops.Select(d => (d.ObjectiveId, d.CreatureTemplateId, d.Chance)));
    }

    [Fact]
    public async Task Leave_the_optional_fields_null_when_the_quest_has_none()
    {
        QuestTemplate quest = Quest();
        quest.ClassRequirement = null;
        quest.RequiredQuestId = null;
        quest.ScriptName = null;
        _quests.FindByIdAsync(Arg.Any<QuestTemplateId>(), Arg.Any<CancellationToken>()).Returns(quest);

        QuestTemplateDto dto = await GetOk(3);

        Assert.Equal(((CharacterClass?)null, (uint?)null, (string?)null), (dto.ClassRequirement, dto.RequiredQuestId, dto.ScriptName));
    }

    [Fact]
    public async Task List_a_page_of_quests_with_their_texts_and_clamp_the_paging()
    {
        _quests.PaginateAsync(1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<QuestTemplate>(1, 50, 1, [Quest()]));

        PagedResult<QuestTemplateDto> page = await MakeSut().List(0, 500, CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("text 33", Assert.Single(page.Items).Title);
        await _texts.Received(1).GetByIdsAsync(Arg.Any<IEnumerable<LocalizedTextId>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Write_the_objective_type_as_its_name()
    {
        _quests.FindByIdAsync(Arg.Any<QuestTemplateId>(), Arg.Any<CancellationToken>()).Returns(Quest());

        string json = JsonSerializer.Serialize(await GetOk(3), JsonSerializerOptions.Web);

        Assert.Contains("\"type\":\"Kill\"", json);
        Assert.Contains("\"type\":\"Collect\"", json);
    }
}
