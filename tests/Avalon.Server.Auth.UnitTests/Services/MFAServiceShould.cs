using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// Recovery-code generation, storage and verification (issue #464). The repository is a
/// substitute backed by a single in-memory row, so setup, confirm and reset run end to end.
/// </summary>
public class MFAServiceShould
{
    private static readonly AccountId AccountId = new(7L);

    private readonly IMfaSetupRepository _repository = Substitute.For<IMfaSetupRepository>();
    private readonly IMFAHashService _hashService = Substitute.For<IMFAHashService>();
    private readonly ISecureRandom _random = Substitute.For<ISecureRandom>();
    private readonly Avalon.Infrastructure.IReplicatedCache _cache = Substitute.For<Avalon.Infrastructure.IReplicatedCache>();
    private MFASetup? _row;

    public MFAServiceShould()
    {
        _repository.FindByAccountIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(_ => _row);
        _repository.UpsertPendingAsync(Arg.Any<MFASetup>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (_row is { Status: MfaSetupStatus.Confirmed }) return false;
                var pending = ci.Arg<MFASetup>();
                pending.Id = _row?.Id ?? Guid.NewGuid();
                _row = pending;
                return true;
            });
        _repository.TryConfirmAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<byte[]>(), Arg.Any<byte[]>(),
                Arg.Any<byte[]>(), Arg.Any<DateTime>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (_row == null || _row.Id != ci.ArgAt<Guid>(0) || _row.Status != MfaSetupStatus.Setup
                    || !_row.Secret.AsSpan().SequenceEqual(ci.ArgAt<byte[]>(1)))
                    return false;
                _row = new MFASetup
                {
                    Id = _row.Id,
                    AccountId = _row.AccountId,
                    Secret = _row.Secret,
                    RecoveryCode1 = ci.ArgAt<byte[]>(2),
                    RecoveryCode2 = ci.ArgAt<byte[]>(3),
                    RecoveryCode3 = ci.ArgAt<byte[]>(4),
                    Status = MfaSetupStatus.Confirmed,
                    CreatedAt = _row.CreatedAt,
                    ConfirmedAt = ci.ArgAt<DateTime>(5),
                    LastAcceptedTotpStep = ci.ArgAt<long>(6),
                };
                return true;
            });
        _repository.When(r => r.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()))
            .Do(ci => { if (_row?.Id == ci.Arg<Guid>()) _row = null; });

        _random.GetBytes(Arg.Any<int>()).Returns(ci => RandomNumberGenerator.GetBytes(ci.Arg<int>()));
    }

    private MFAService CreateService() =>
        new(NullLoggerFactory.Instance, _repository, _hashService, _random, _cache);

    private static Account NewAccount() => new()
    {
        Id = AccountId,
        Username = "player",
        Email = "player@example.com",
        Salt = [],
        Verifier = [],
        JoinDate = DateTime.UtcNow,
    };

    private async Task<string[]> SetUpAndConfirmAsync(MFAService service)
    {
        var setup = await service.SetupMFAAsync(NewAccount(), "Avalon");
        Assert.True(setup.Success);

        var totp = new Totp(_row!.Secret).ComputeTotp();
        var confirm = await service.ConfirmMFAAsync(AccountId, totp);
        Assert.True(confirm.Success);
        Assert.NotNull(confirm.RecoveryCodes);
        Assert.Equal(3, confirm.RecoveryCodes!.Length);
        return confirm.RecoveryCodes;
    }

    private static string ChangeLastCharacter(string code)
    {
        var replacement = code[^1] == '0' ? '1' : '0';
        return code[..^1] + replacement;
    }

    [Fact]
    public async Task Generate_each_recovery_code_from_the_secure_random_bytes()
    {
        var service = CreateService();
        await service.SetupMFAAsync(NewAccount(), "Avalon");

        _random.ClearReceivedCalls();
        _random.GetBytes(Arg.Any<int>()).Returns(
            new byte[10],
            Enumerable.Repeat((byte)0xFF, 10).ToArray(),
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB });

        var confirm = await service.ConfirmMFAAsync(AccountId, new Totp(_row!.Secret).ComputeTotp());

        Assert.True(confirm.Success);
        Assert.Equal(
            new[] { "0000-0000-0000-0000", "ZZZZ-ZZZZ-ZZZZ-ZZZZ", "VTPV-XVR1-4D2P-F2DB" },
            confirm.RecoveryCodes);
        // 80 bits of CSPRNG output per code, three codes.
        _random.Received(3).GetBytes(10);
    }

    [Fact]
    public async Task Store_a_hash_of_each_recovery_code_and_never_the_code_itself()
    {
        var shown = await SetUpAndConfirmAsync(CreateService());
        var stored = new[] { _row!.RecoveryCode1, _row.RecoveryCode2, _row.RecoveryCode3 };

        for (var i = 0; i < 3; i++)
        {
            Assert.NotEqual(Encoding.UTF8.GetBytes(shown[i]), stored[i]);
            Assert.NotEqual(Encoding.UTF8.GetBytes(shown[i].Replace("-", "")), stored[i]);
            Assert.Equal(SHA256.HashSizeInBytes, stored[i].Length);
        }
    }

    [Fact]
    public async Task Reset_when_all_recovery_codes_are_correct()
    {
        var service = CreateService();
        var codes = await SetUpAndConfirmAsync(service);

        var result = await service.ResetMFAAsync(AccountId, codes[0], codes[1], codes[2]);

        Assert.True(result.Success);
        Assert.Equal(MFAOperationResult.Success, result.Status);
    }

    [Fact]
    public async Task Reset_when_codes_are_typed_in_lower_case_without_separators()
    {
        var service = CreateService();
        var codes = await SetUpAndConfirmAsync(service);

        var result = await service.ResetMFAAsync(AccountId,
            codes[0].ToLowerInvariant(), codes[1].Replace("-", ""), $" {codes[2]} ");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Reject_a_wrong_recovery_code()
    {
        var service = CreateService();
        var codes = await SetUpAndConfirmAsync(service);

        var result = await service.ResetMFAAsync(AccountId, codes[0], "0000-0000-0000-0000", codes[2]);

        Assert.False(result.Success);
        Assert.Equal(MFAOperationResult.InvalidCode, result.Status);
        Assert.NotNull(_row);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Reject_a_recovery_code_that_differs_only_in_its_last_character(int index)
    {
        var service = CreateService();
        var codes = await SetUpAndConfirmAsync(service);
        var attempt = (string[])codes.Clone();
        attempt[index] = ChangeLastCharacter(attempt[index]);

        var result = await service.ResetMFAAsync(AccountId, attempt[0], attempt[1], attempt[2]);

        Assert.False(result.Success);
        Assert.Equal(MFAOperationResult.InvalidCode, result.Status);
        Assert.NotNull(_row);
    }

    [Fact]
    public async Task Reject_recovery_codes_that_were_already_used()
    {
        var service = CreateService();
        var codes = await SetUpAndConfirmAsync(service);

        var first = await service.ResetMFAAsync(AccountId, codes[0], codes[1], codes[2]);
        var second = await service.ResetMFAAsync(AccountId, codes[0], codes[1], codes[2]);

        Assert.True(first.Success);
        Assert.False(second.Success);
    }

    [Fact]
    public async Task Reject_legacy_plaintext_recovery_codes()
    {
        // Rows written before #464 hold the code itself as UTF-8 bytes. They are invalidated.
        var legacy = new[] { "ABCD-EF01-2345", "6789-ABCD-EF01", "2345-6789-ABCD" };
        _row = ConfirmedRow(
            Encoding.UTF8.GetBytes(legacy[0]),
            Encoding.UTF8.GetBytes(legacy[1]),
            Encoding.UTF8.GetBytes(legacy[2]));

        var result = await CreateService().ResetMFAAsync(AccountId, legacy[0], legacy[1], legacy[2]);

        Assert.False(result.Success);
        Assert.Equal(MFAOperationResult.InvalidCode, result.Status);
        Assert.NotNull(_row);
    }

    [Fact]
    public async Task Reject_empty_input_against_a_row_with_no_recovery_codes()
    {
        _row = ConfirmedRow([], [], []);

        var result = await CreateService().ResetMFAAsync(AccountId, "", "", "");

        Assert.False(result.Success);
        Assert.NotNull(_row);
    }

    private static MFASetup ConfirmedRow(byte[] r1, byte[] r2, byte[] r3) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = AccountId,
        Secret = KeyGeneration.GenerateRandomKey(32),
        RecoveryCode1 = r1,
        RecoveryCode2 = r2,
        RecoveryCode3 = r3,
        Status = MfaSetupStatus.Confirmed,
        CreatedAt = DateTime.UtcNow,
        ConfirmedAt = DateTime.UtcNow,
    };
}
