using System.Net;
using System.Net.Http.Json;
using Avalon.Api.Contract;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Extensions;
using Avalon.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// #714: /world/{worldId}/quest-template through the real pipeline (authentication, the world check, the
/// GameMaster policy) and the real repositories over a seeded SQLite world.
/// </summary>
public sealed class QuestTemplateRouteShould : IAsyncLifetime
{
    private const ushort Open = 1;    // configured, Player
    private const ushort Staff = 2;   // configured, Admin only

    private readonly SqliteWorlds _sqlite = new(Open, Staff);
    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync()
    {
        Row(Open, AccountAccessLevel.Player);
        Row(Staff, AccountAccessLevel.Admin);

        _host = await ApiAuthHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(new WorldDatabases(
            [
                new ConfiguredWorld(new WorldId(Open), "Host=w1", "Host=c1"),
                new ConfiguredWorld(new WorldId(Staff), "Host=w2", "Host=c2"),
            ]));
            services.AddSingleton<IWorldDbContextFactory>(_sqlite);
            services.AddWorldRepositories();
            services.AddSingleton(_authWorlds);
        });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _sqlite.Dispose();
    }

    private void Row(ushort id, AccountAccessLevel required) =>
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntity
            {
                Id = new WorldId(id), Name = $"World{id}", AccessLevelRequired = required,
                Host = "h", MinVersion = "0.0.1", Version = "0.0.1",
            });

    private Task<HttpResponseMessage> Get(string path, AccountAccessLevel level)
    {
        Account account = ApiAuthHost.MakeAccount(level);
        _host.AccountNowIs(account);
        return _host.GetAsync(path, ApiAuthHost.Mint(account));
    }

    /// <summary>The seeded "The Alpha's Howl": three stages, a talk objective, an item reward, English texts.</summary>
    [Fact]
    public async Task Serve_a_seeded_quest_whole_to_a_game_master()
    {
        HttpResponseMessage response = await Get($"/world/{Open}/quest-template/3", AccountAccessLevel.GameMaster);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuestTemplateDto quest = (await response.Content.ReadFromJsonAsync<QuestTemplateDto>())!;
        Assert.Equal(("The Alpha's Howl", 2ul, 2ul, (ushort)2, (uint?)2), (quest.Title, quest.GiverCreatureTemplateId,
            quest.EnderCreatureTemplateId, quest.LevelRequirement, quest.RequiredQuestId));
        Assert.Equal([0, 1, 2], quest.Stages.Select(s => s.Sequence));
        Assert.Equal("Tell Marta what you have seen.", quest.Stages[1].Text);
        QuestObjectiveTemplateDto talk = Assert.Single(quest.Stages[1].Objectives);
        Assert.Equal((303u, Avalon.Api.Contract.QuestObjectiveType.Talk, (ulong?)11, 1u, "Speak with Marta Ledgerwell"),
            (talk.Id, talk.Type, talk.CreatureTemplateId, talk.Count, talk.Text));
        Assert.Equal([(58ul, 1u)], quest.ItemRewards.Select(r => (r.ItemTemplateId, r.Count)));
        Assert.Equal((600u, 400ul), (quest.RewardExperience, quest.RewardMoney));
    }

    [Fact]
    public async Task Carry_a_collect_objectives_drops()
    {
        QuestTemplateDto quest = (await (await Get($"/world/{Open}/quest-template/2", AccountAccessLevel.GameMaster))
            .Content.ReadFromJsonAsync<QuestTemplateDto>())!;

        Assert.Equal([(201u, 4ul, 60f)], quest.ItemDrops.Select(d => (d.ObjectiveId, d.CreatureTemplateId, d.Chance)));
    }

    [Fact]
    public async Task List_the_seeded_quests_in_id_order()
    {
        HttpResponseMessage response = await Get($"/world/{Open}/quest-template", AccountAccessLevel.GameMaster);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PagedResult<QuestTemplateDto> page = (await response.Content.ReadFromJsonAsync<PagedResult<QuestTemplateDto>>())!;
        Assert.Equal([1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u], page.Items.Select(q => q.Id));
        Assert.Equal(8, page.TotalCount);
        Assert.Equal("Thinning the Herd", page.Items[0].Title);
    }

    [Fact]
    public async Task Answer_404_for_an_unknown_quest() =>
        Assert.Equal(HttpStatusCode.NotFound,
            (await Get($"/world/{Open}/quest-template/999", AccountAccessLevel.GameMaster)).StatusCode);

    [Fact]
    public async Task Refuse_a_player() =>
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Get($"/world/{Open}/quest-template/1", AccountAccessLevel.Player)).StatusCode);

    [Fact]
    public async Task Answer_404_for_a_world_the_caller_may_not_enter() =>
        Assert.Equal(HttpStatusCode.NotFound,
            (await Get($"/world/{Staff}/quest-template/1", AccountAccessLevel.GameMaster)).StatusCode);

    [Fact]
    public async Task Answer_404_for_an_unconfigured_world() =>
        Assert.Equal(HttpStatusCode.NotFound,
            (await Get("/world/9/quest-template/1", AccountAccessLevel.GameMaster)).StatusCode);
}
