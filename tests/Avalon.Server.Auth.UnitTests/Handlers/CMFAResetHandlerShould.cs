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

public class CMFAResetHandlerShould
{
    private readonly IMFAService _mfaService = Substitute.For<IMFAService>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();

    private CMFAResetHandler CreateHandler() =>
        new(NullLoggerFactory.Instance, _mfaService, _accountRepository);

    public CMFAResetHandlerShould()
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
    public async Task SendSuccess_WhenRecoveryCodesMatch()
    {
        _mfaService.ResetMFAAsync(Arg.Any<AccountId>(), "r1", "r2", "r3", Arg.Any<CancellationToken>())
            .Returns(new MFAResetResult(true, MFAOperationResult.Success));

        var ctx = new AuthPacketContext<CMFAResetPacket>
        {
            Packet = new CMFAResetPacket { RecoveryCode1 = "r1", RecoveryCode2 = "r2", RecoveryCode3 = "r3" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendInvalidCode_WhenRecoveryCodesWrong()
    {
        _mfaService.ResetMFAAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAResetResult(false, MFAOperationResult.InvalidCode));

        var ctx = new AuthPacketContext<CMFAResetPacket>
        {
            Packet = new CMFAResetPacket { RecoveryCode1 = "bad1", RecoveryCode2 = "bad2", RecoveryCode3 = "bad3" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task CloseConnection_WhenNotAuthenticated()
    {
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new AuthPacketContext<CMFAResetPacket>
        {
            Packet = new CMFAResetPacket { RecoveryCode1 = "r1", RecoveryCode2 = "r2", RecoveryCode3 = "r3" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Close();
        await _mfaService.DidNotReceive().ResetMFAAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
