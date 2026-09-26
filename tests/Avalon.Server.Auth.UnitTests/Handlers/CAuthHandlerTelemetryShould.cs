using Avalon.Infrastructure.Login;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Telemetry;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

/// <summary>avalon.auth.logins: one count per password login, by the result the client got.</summary>
public partial class CAuthHandlerShould
{
    [Theory]
    [InlineData(AuthResult.SUCCESS, "success")]
    [InlineData(AuthResult.INVALID_CREDENTIALS, "invalid_credentials")]
    [InlineData(AuthResult.LOCKED, "locked")]
    [InlineData(AuthResult.MFA_REQUIRED, "mfa_required")]
    [InlineData(AuthResult.ALREADY_CONNECTED, "already_connected")]
    [InlineData(AuthResult.BANNED, "banned")]
    [InlineData(AuthResult.DEACTIVATED, "deactivated")]
    public void Name_each_login_result_for_the_counter(AuthResult result, string tag)
    {
        Assert.Equal(tag, LoginTelemetry.Tag(result));
    }

    [Theory]
    [InlineData("rate_limited", LogLevel.Debug)]
    [InlineData("invalid_credentials", LogLevel.Information)]
    [InlineData("success", LogLevel.Information)]
    public void Log_refusals_by_the_budget_at_debug_so_a_flood_does_not_flood_the_logs(string result, LogLevel level)
    {
        Assert.Equal(level, LoginTelemetry.LogLevelFor(result));
    }

    [Fact]
    public async Task Count_a_login_without_credentials_as_invalid_credentials()
    {
        using LoginCounterProbe probe = new();
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "", Password = TestPasswords.Wrong },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(["invalid_credentials"], probe.Results);
    }

    [Fact]
    public async Task Count_a_successful_login_as_success()
    {
        using LoginCounterProbe probe = new();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = TestPasswords.Valid },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(["success"], probe.Results);
    }

    [Fact]
    public async Task Count_a_wrong_password_as_invalid_credentials()
    {
        using LoginCounterProbe probe = new();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = TestPasswords.Wrong },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(["invalid_credentials"], probe.Results);
    }

    [Fact]
    public async Task Count_a_source_past_its_budget_as_rate_limited_though_the_client_is_told_locked()
    {
        using LoginCounterProbe probe = new();
        _cache.IncrementAsync(SourceKey, Arg.Any<TimeSpan>()).Returns(11L);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());

        await LogInAsync(CreateHandler(HardeningOptions(perSource: 10), Substitute.For<IPasswordVerifier>()), password: TestPasswords.Wrong);

        Assert.Equal(AuthResult.LOCKED, SentResult());
        Assert.Equal([LoginTelemetry.RateLimited], probe.Results);
    }
}
