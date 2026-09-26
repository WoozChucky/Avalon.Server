using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Abstractions;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CMFAConfirmHandlerShould
{
    private readonly IMFAService _mfaService = Substitute.For<IMFAService>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();

    private CMFAConfirmHandler CreateHandler() =>
        new(NullLoggerFactory.Instance, _mfaService, _accountRepository);

    public CMFAConfirmHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.AccountId.Returns(new AccountId(1L));
        // The connection's account, Active and at the version its login proved (#495 review).
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => new Account
            {
                Id = ci.ArgAt<AccountId>(0), Username = "TESTUSER", Email = "t@t", Salt = [1], Verifier = [2],
                JoinDate = DateTime.UtcNow,
            });
    }

    [Fact]
    public async Task SendRecoveryCodes_WhenCodeIsValid()
    {
        var codes = new[] { "code1", "code2", "code3" };
        _mfaService.ConfirmMFAAsync(Arg.Any<AccountId>(), Arg.Any<int>(), "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAConfirmResult(true, codes, MFAOperationResult.Success));

        var ctx = new AuthPacketContext<CMFAConfirmPacket>
        {
            Packet = new CMFAConfirmPacket { Code = "123456" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendInvalidCode_WhenCodeIsWrong()
    {
        _mfaService.ConfirmMFAAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAConfirmResult(false, null, MFAOperationResult.InvalidCode));

        var ctx = new AuthPacketContext<CMFAConfirmPacket>
        {
            Packet = new CMFAConfirmPacket { Code = "wrong" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task CloseConnection_WhenNotAuthenticated()
    {
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new AuthPacketContext<CMFAConfirmPacket>
        {
            Packet = new CMFAConfirmPacket { Code = "123456" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Close();
        await _mfaService.DidNotReceive().ConfirmMFAAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// #495 re-review: the credentials changed between the guard's read and the write. The handler
    /// closes the connection, as the guard would, and sends no result.
    /// </summary>
    [Fact]
    public async Task CloseTheConnection_WhenTheCredentialsChangedBeforeTheWrite()
    {
        _connection.CredentialsVersion.Returns(0);
        _mfaService.ConfirmMFAAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAConfirmResult(false, null, MFAOperationResult.Error, CredentialsChanged: true));

        await CreateHandler().ExecuteAsync(new AuthPacketContext<CMFAConfirmPacket>
        {
            Packet = new CMFAConfirmPacket { Code = "123456" },
            Connection = _connection
        });

        _connection.Received(1).Close();
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }
}
