using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_CHARACTER_LEAVE (#663) at the handler: every state gets its documented answer, the success
/// answer waits for the logout save, and a failed save closes the connection instead of answering.
/// The despawn itself is the world's, covered by CharacterLeaveShould against the real world.
/// </summary>
public class CharacterLeaveHandlerShould
{
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection, ICharacterLeaveControl>();
    private readonly List<NetworkPacket> _sent = [];
    private readonly List<(Task<bool> Task, Action<bool> Callback)> _continuations = [];
    private readonly CharacterLeaveHandler _handler;

    public CharacterLeaveHandlerShould()
    {
        _connection.AccountId.Returns(new AccountId(42L));
        _connection.IsConnected.Returns(true);
        // A substitute answers an interface-typed property with another substitute, never null.
        _connection.Character.Returns((ICharacter?)null);
        _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        _connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => _sent.Add(ci.Arg<NetworkPacket>()));
        _connection.When(c => c.EnqueueContinuation(Arg.Any<Task<bool>>(), Arg.Any<Action<bool>>()))
            .Do(ci => _continuations.Add((ci.Arg<Task<bool>>(), ci.Arg<Action<bool>>())));
        Control.TryBeginLeave().Returns(true);

        _handler = new CharacterLeaveHandler(NullLogger<CharacterLeaveHandler>.Instance, _world);
    }

    private ICharacterLeaveControl Control => (ICharacterLeaveControl)_connection;

    private void Leave() => _handler.Execute(_connection, new CCharacterLeavePacket());

    private List<CharacterLeaveResult> Results() =>
        TestTown.Read<SCharacterLeaveResultPacket>(_sent, NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT)
            .Select(p => p.Result).ToList();

    private List<SDisconnectPacket> Disconnects() =>
        TestTown.Read<SDisconnectPacket>(_sent, NetworkPacketType.SMSG_DISCONNECT);

    /// <summary>What the tick does with a continuation once its task has finished.</summary>
    private async Task RunContinuationsAsync()
    {
        foreach ((Task<bool> task, Action<bool> callback) in _continuations.ToList())
            callback(await task.WaitAsync(TimeSpan.FromSeconds(5)));
        _continuations.Clear();
    }

    private void HoldCharacter() => _connection.Character.Returns(New(7));

    /// <summary>
    /// A leave that cannot start is answered with the state that stops it and changes nothing. A character built and
    /// waiting on its client's load report is not in the world yet, so it is Selecting too.
    /// </summary>
    [Theory]
    [InlineData(false, false, CharacterLeaveResult.NoCharacter)]
    [InlineData(true, false, CharacterLeaveResult.Selecting)]
    [InlineData(false, true, CharacterLeaveResult.Selecting)]
    public void Answer_a_leave_that_cannot_start_and_change_nothing(bool selecting, bool waitingOnClient,
        CharacterLeaveResult expected)
    {
        _connection.SelectInProgress.Returns(selecting);
        if (waitingOnClient)
            _connection.PendingSpawn.Returns(new PendingSpawn(New(7), Substitute.For<IMapInstance>(), 1));

        Leave();

        Assert.Equal([expected], Results());
        _world.DidNotReceiveWithAnyArgs().LeaveWorldAsync(default!);
        Control.DidNotReceive().TryBeginLeave();
        _connection.DidNotReceive().Close();
        _connection.DidNotReceive().CancelSelect();
        _connection.DidNotReceive().TakePendingSpawn();
    }

    [Fact]
    public void Close_an_unauthenticated_connection()
    {
        _connection.AccountId.Returns((AccountId?)null);
        HoldCharacter();

        Leave();

        _connection.Received(1).Close();
        Assert.Empty(Results());
        _world.DidNotReceiveWithAnyArgs().LeaveWorldAsync(default!);
    }

    /// <summary>A kicked or closing connection's own close despawns it; there is nobody to answer.</summary>
    [Fact]
    public void Ignore_a_leave_from_a_closing_connection()
    {
        _connection.IsClosing.Returns(true);
        HoldCharacter();

        Leave();

        Assert.Empty(_sent);
        _world.DidNotReceiveWithAnyArgs().LeaveWorldAsync(default!);
    }

    [Fact]
    public async Task Answer_Left_only_once_the_logout_save_has_committed()
    {
        HoldCharacter();
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _world.LeaveWorldAsync(_connection).Returns(save.Task);

        Leave();

        Control.Received(1).TryBeginLeave();
        Control.Received(1).ResetCharacterState();
        await _world.Received(1).LeaveWorldAsync(_connection);
        Assert.Empty(_sent);
        Control.DidNotReceive().EndLeave();

        save.SetResult(true);
        await RunContinuationsAsync();

        Assert.Equal([CharacterLeaveResult.Left], Results());
        Control.Received(1).EndLeave();
        _connection.DidNotReceive().Close();
    }

    /// <summary>
    /// The character's last state is not known to be written, so the session is not handed back to
    /// character selection: a logout save that fails, or a leave that throws, closes it with a reason,
    /// and no success is ever sent.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_with_CharacterSaveFailed_and_not_answer_when_the_logout_save_fails(bool throws)
    {
        HoldCharacter();
        _world.LeaveWorldAsync(_connection).Returns(throws
            ? Task.FromException<bool>(new InvalidOperationException("boom"))
            : Task.FromResult(false));

        Leave();
        await RunContinuationsAsync();

        Assert.Empty(Results());
        SDisconnectPacket disconnect = Assert.Single(Disconnects());
        Assert.Equal(DisconnectReason.CharacterSaveFailed, disconnect.ReasonCode);
        Assert.Equal(CharacterLeaveHandler.SaveFailedMessage, disconnect.Reason);
        _connection.Received(1).Close();
        Control.Received(1).EndLeave();
    }

    /// <summary>A connection that dropped, or was kicked, while its character was leaving is told nothing.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Not_answer_a_connection_that_went_away_while_leaving(bool connected, bool closing)
    {
        HoldCharacter();
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _world.LeaveWorldAsync(_connection).Returns(save.Task);

        Leave();
        _connection.IsConnected.Returns(connected);
        _connection.IsClosing.Returns(closing);
        save.SetResult(true);
        await RunContinuationsAsync();

        Assert.Empty(_sent);
        _connection.DidNotReceive().Close();
        Control.Received(1).EndLeave();
    }
}
