using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;
using WorldId = Avalon.Domain.Auth.WorldId;

namespace Avalon.Api.UnitTests.Services;

/// <summary>GET /character (#523): the caller's characters on every world it may enter.</summary>
public class AccountCharactersServiceShould
{
    private static readonly AccountId Owner = new(7);

    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private readonly IWorldRepositories _perWorld = Substitute.For<IWorldRepositories>();
    private readonly List<WorldEntity> _rows = [];
    private readonly List<ConfiguredWorld> _configured = [];
    private readonly CapturingLogger _logger = new();

    private ICharacterRepository GivenWorld(ushort id, string name, AccountAccessLevel required, bool configured,
        params string[] characters)
    {
        _rows.Add(new WorldEntity
        {
            Id = new WorldId(id), Name = name, AccessLevelRequired = required,
            Host = "h", MinVersion = "0.0.1", Version = "0.0.1",
        });
        if (configured) _configured.Add(new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}"));

        ICharacterRepository repository = Substitute.For<ICharacterRepository>();
        repository.FindByAccountAsync(Owner, Arg.Any<CancellationToken>()).Returns(characters
            .Select((character, i) => new Character
            {
                Id = new CharacterId((uint)(id * 100 + i)), AccountId = Owner, Name = character, CreationDate = DateTime.UtcNow,
            })
            .ToList());
        _perWorld.Characters(Arg.Is<WorldId>(w => w.Value == id)).Returns(repository);
        return repository;
    }

    private AccountCharactersService Sut(params ushort[] unavailable)
    {
        _authWorlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_rows);
        WorldDatabases databases = new(_configured);
        foreach (ushort id in unavailable) databases.MarkUnavailable(new WorldId(id));
        return new AccountCharactersService(_authWorlds, databases, _perWorld, _logger);
    }

    [Fact]
    public async Task Aggregate_characters_across_worlds_with_their_world()
    {
        GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true, "Nym");
        GivenWorld(2, "Asthoria", AccountAccessLevel.Player, configured: true, "Kel", "Zed");

        CharacterListDto list = await Sut().GetAsync(Owner, AccountAccessLevel.Player);

        Assert.Equal(["Nym", "Kel", "Zed"], list.Characters.Select(c => c.Name));
        Assert.Equal(new ushort[] { 1, 2, 2 }, list.Characters.Select(c => c.WorldId));
        Assert.Equal(["Development", "Asthoria", "Asthoria"], list.Characters.Select(c => c.WorldName));
        Assert.Empty(list.UnavailableWorlds);
    }

    [Fact]
    public async Task Skip_worlds_the_caller_may_not_enter_without_listing_them()
    {
        GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true, "Nym");
        GivenWorld(3, "Staff", AccountAccessLevel.Admin, configured: true, "Hidden");

        CharacterListDto list = await Sut(unavailable: 3).GetAsync(Owner, AccountAccessLevel.Player);

        Assert.Equal(["Nym"], list.Characters.Select(c => c.Name));
        Assert.Empty(list.UnavailableWorlds);
        _perWorld.DidNotReceive().Characters(Arg.Is<WorldId>(w => w.Value == 3));
    }

    [Fact]
    public async Task List_an_unavailable_world_without_reading_it()
    {
        GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true, "Nym");
        GivenWorld(2, "Asthoria", AccountAccessLevel.Player, configured: true, "Kel");

        CharacterListDto list = await Sut(unavailable: 2).GetAsync(Owner, AccountAccessLevel.Player);

        Assert.Equal(["Nym"], list.Characters.Select(c => c.Name));
        Assert.Equal(new ushort[] { 2 }, list.UnavailableWorlds);
        _perWorld.DidNotReceive().Characters(Arg.Is<WorldId>(w => w.Value == 2));
    }

    [Fact]
    public async Task List_a_world_whose_read_fails_and_still_answer()
    {
        GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true, "Nym");
        ICharacterRepository broken = GivenWorld(2, "Asthoria", AccountAccessLevel.Player, configured: true);
        broken.FindByAccountAsync(Owner, Arg.Any<CancellationToken>())
            .ThrowsAsync(new NpgsqlException("Failed to connect to Host=secret;Port=5433"));

        CharacterListDto list = await Sut().GetAsync(Owner, AccountAccessLevel.Player);

        Assert.Equal(["Nym"], list.Characters.Select(c => c.Name));
        Assert.Equal(new ushort[] { 2 }, list.UnavailableWorlds);
        // A driver's message can carry a host: only the exception's type may reach the log.
        Assert.NotEmpty(_logger.Entries);
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain("secret", entry, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Tournament)]
    public async Task Hide_an_admin_world_from_a_ptr_or_tournament_caller(AccountAccessLevel caller)
    {
        // PTR (32) and Tournament (16) are numerically above Admin (4): the world rule is a mask test.
        GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true, "Nym");
        GivenWorld(3, "Staff", AccountAccessLevel.Admin, configured: true, "Hidden");

        CharacterListDto list = await Sut(unavailable: 3).GetAsync(Owner, caller);

        Assert.Equal(["Nym"], list.Characters.Select(c => c.Name));
        Assert.Empty(list.UnavailableWorlds);
        _perWorld.DidNotReceive().Characters(Arg.Is<WorldId>(w => w.Value == 3));
    }

    [Fact]
    public async Task Show_a_ptr_caller_both_a_player_world_and_a_ptr_world()
    {
        GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true, "Nym");
        GivenWorld(5, "Test Realm", AccountAccessLevel.PTR, configured: true, "Tess");

        CharacterListDto list = await Sut().GetAsync(Owner, AccountAccessLevel.PTR);

        Assert.Equal(["Nym", "Tess"], list.Characters.Select(c => c.Name));
        Assert.Equal(new ushort[] { 1, 5 }, list.Characters.Select(c => c.WorldId));
        Assert.Empty(list.UnavailableWorlds);
    }

    [Fact]
    public async Task Leave_out_a_world_this_api_is_not_configured_for()
    {
        GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true, "Nym");
        GivenWorld(4, "Elsewhere", AccountAccessLevel.Player, configured: false, "Far");

        CharacterListDto list = await Sut().GetAsync(Owner, AccountAccessLevel.Player);

        Assert.Equal(["Nym"], list.Characters.Select(c => c.Name));
        Assert.Empty(list.UnavailableWorlds);
    }

    [Fact]
    public async Task Stop_when_the_caller_cancels()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        ICharacterRepository repository = GivenWorld(1, "Development", AccountAccessLevel.Player, configured: true);
        repository.FindByAccountAsync(Owner, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Sut().GetAsync(Owner, AccountAccessLevel.Player, cancelled.Token));
    }

    /// <summary>Every entry as it would be written: the formatted message and any exception attached.</summary>
    private sealed class CapturingLogger : ILogger<AccountCharactersService>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
