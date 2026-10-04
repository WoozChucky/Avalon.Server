using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class AccountConsolidationServiceShould
{
    private readonly IAccountConsolidationRepository _operations = Substitute.For<IAccountConsolidationRepository>();
    private readonly IWorldRepository _registry = Substitute.For<IWorldRepository>();
    private readonly IWorldRepositories _worlds = Substitute.For<IWorldRepositories>();
    private readonly ICharacterConsolidationRepository _first = Substitute.For<ICharacterConsolidationRepository>();
    private readonly ICharacterConsolidationRepository _second = Substitute.For<ICharacterConsolidationRepository>();
    private readonly List<string> _order = [];
    private readonly AccountConsolidation _operation = new() { Id = Guid.NewGuid(), SourceAccountId = new(7), TargetAccountId = new(8),
        SteamSubject = "76561198000000001", Worlds = [new() { WorldId = 1 }, new() { WorldId = 2 }] };
    private readonly AccountConsolidationService _service;
    public AccountConsolidationServiceShould()
    {
        _registry.FindAllAsync(false, Arg.Any<CancellationToken>()).Returns([World(1), World(2)]);
        _operations.FindAsync(_operation.Id, Arg.Any<CancellationToken>()).Returns(_operation);
        _worlds.CharacterConsolidations(new WorldId(1)).Returns(_first);
        _worlds.CharacterConsolidations(new WorldId(2)).Returns(_second);
        foreach (var (id, repository) in new[] { (1, _first), (2, _second) })
        {
            repository.PrepareAsync(_operation.Id, _operation.SourceAccountId, _operation.TargetAccountId, Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("prepare" + id); return true; });
            repository.TransferAsync(_operation.Id, _operation.SourceAccountId, _operation.TargetAccountId, Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("transfer" + id); return new CharacterConsolidationResult(null, id); });
            repository.ReleaseTargetAsync(_operation.Id, _operation.SourceAccountId, _operation.TargetAccountId, Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("release" + id); return true; });
        }
        _operations.RecordTransferAsync(_operation.Id, Arg.Any<WorldId>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => {
            var world = _operation.Worlds.Single(w => w.WorldId == call.Arg<WorldId>().Value);
            world.TransferredCharacters = call.Arg<int>(); world.TransferredAt = DateTime.UtcNow; _order.Add("record" + world.WorldId); return true;
        });
        _operations.FinalizeAsync(_operation.Id, Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("finalize"); _operation.State = AccountConsolidationState.Finalized; return true; });
        _operations.RecordGuardReleasedAsync(_operation.Id, Arg.Any<WorldId>(), Arg.Any<CancellationToken>()).Returns(call => {
            _operation.Worlds.Single(w => w.WorldId == call.Arg<WorldId>().Value).GuardReleasedAt = DateTime.UtcNow; return true;
        });
        _operations.CompleteAsync(_operation.Id, Arg.Any<CancellationToken>()).Returns(_ => { _order.Add("complete"); _operation.State = AccountConsolidationState.Completed; return true; });
        _service = new(_operations, _registry, new WorldDatabases([new(new(1), "world1", "chars1"), new(new(2), "world2", "chars2")]), _worlds);
    }
    private Task<AccountConsolidationReply> Resume() => _service.ResumeAsync(_operation.Id, _operation.TargetAccountId, CancellationToken.None);
    [Fact]
    public async Task Prepare_all_worlds_before_transfer_and_finalize_only_after_durable_world_progress()
    {
        var reply = await Resume();
        Assert.Null(reply.Error);
        Assert.Equal("completed", reply.State);
        Assert.Equal(new[] { "prepare1", "prepare2", "transfer1", "record1", "transfer2", "record2", "finalize", "release1", "release2", "complete" }, _order);
        Assert.Equal(3L, reply.TransferredCharacters);
    }
    [Fact]
    public async Task Resume_after_one_world_commits_and_another_is_still_draining_without_early_finalization()
    {
        _second.TransferAsync(_operation.Id, _operation.SourceAccountId, _operation.TargetAccountId, Arg.Any<CancellationToken>()).Returns(new CharacterConsolidationResult("WAITING_FOR_SESSION"));
        Assert.Equal("waiting_for_sessions", (await Resume()).State);
        Assert.DoesNotContain("finalize", _order);
        _order.Clear();
        _second.TransferAsync(_operation.Id, _operation.SourceAccountId, _operation.TargetAccountId, Arg.Any<CancellationToken>()).Returns(new CharacterConsolidationResult(null, 2));
        Assert.Equal("completed", (await Resume()).State);
        Assert.DoesNotContain("transfer1", _order);
    }
    [Fact]
    public async Task Recover_world_commit_when_its_auth_progress_write_was_lost()
    {
        _operations.RecordTransferAsync(_operation.Id, new WorldId(1), 1, Arg.Any<CancellationToken>()).Returns(false);
        Assert.NotNull((await Resume()).Error);
        Assert.DoesNotContain("finalize", _order);
        _operations.RecordTransferAsync(_operation.Id, new WorldId(1), 1, Arg.Any<CancellationToken>()).Returns(_ => { _operation.Worlds[0].TransferredAt = DateTime.UtcNow; _operation.Worlds[0].TransferredCharacters = 1; return true; });
        Assert.Equal("completed", (await Resume()).State);
        Assert.Equal(3, _operation.Worlds.Sum(w => w.TransferredCharacters));
    }
    [Fact]
    public async Task Deny_another_browser_account_and_incomplete_world_configuration_before_preparing_guards()
    {
        Assert.NotNull((await _service.ResumeAsync(_operation.Id, new AccountId(9), CancellationToken.None)).Error);
        Assert.Empty(_order);
        _registry.FindAllAsync(false, Arg.Any<CancellationToken>()).Returns([World(1), World(2), World(3)]);
        Assert.Equal("WORLD_CONFIG_INCOMPLETE", (await Resume()).Error);
        Assert.Empty(_order);
    }
    private static Avalon.Domain.Auth.World World(ushort id) => new() { Id = new(id), Name = "World" + id, Host = "localhost", Port = 21000 + id, MinVersion = "1", Version = "1" };
}
