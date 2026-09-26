using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Avalon.Server.Auth.UnitTests.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// The code rules the REST MFA verify runs, through the real <see cref="MFAService"/> and the
/// shared <see cref="MfaLoginPolicy"/>: a ±1 step window, each step accepted once (#471), and one
/// winner per hash (#478). Before the last, the hash was read in one step and deleted in another,
/// so two verifies sent together with one hash, each with a code the window accepts, both passed
/// and both got a session.
/// </summary>
public sealed class RestMfaCodeShould
{
    private const string Hash = "HASH";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly Account _account = new()
    {
        Id = new AccountId(7), Username = "CALLER", Email = "c@avalon.monster", Salt = [1], Verifier = [2],
        JoinDate = DateTime.UtcNow,
    };

    private readonly byte[] _secret = KeyGeneration.GenerateRandomKey(32);
    private readonly Guid _row = Guid.NewGuid();
    private readonly object _gate = new();
    private long _lastAcceptedStep;
    private bool _hashLive = true;
    private int _setupReads;
    private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _earlierStepTried = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IMfaSetupRepository _setups = Substitute.For<IMfaSetupRepository>();
    private readonly IMFAHashService _hashes = Substitute.For<IMFAHashService>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly CounterCache _counters = new();

    /// <summary>When set, each code's setup read waits until two verifies have both read it.</summary>
    private bool _holdReads;

    /// <summary>When set, the later of two steps is not accepted before the earlier one has been tried.</summary>
    private long? _earlierStep;

    public RestMfaCodeShould()
    {
        _accounts.FindByIdAsync(_account.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_account);

        _setups.FindByAccountIdAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            MFASetup row;
            lock (_gate)
                row = new MFASetup
                {
                    Id = _row, AccountId = _account.Id, Secret = _secret, RecoveryCode1 = [], RecoveryCode2 = [],
                    RecoveryCode3 = [], Status = MfaSetupStatus.Confirmed, CreatedAt = DateTime.UtcNow,
                    ConfirmedAt = DateTime.UtcNow, LastAcceptedTotpStep = _lastAcceptedStep,
                };
            if (_holdReads)
            {
                if (Interlocked.Increment(ref _setupReads) == 2) _bothRead.TrySetResult();
                await _bothRead.Task.WaitAsync(Bound);
            }

            return (MFASetup?)row;
        });

        // The conditional write TryAcceptTotpStepAsync is: only a later step than the stored one.
        _setups.TryAcceptTotpStepAsync(_row, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(async ci =>
        {
            long step = ci.ArgAt<long>(1);
            if (_earlierStep is { } earlier)
            {
                if (step == earlier) _earlierStepTried.TrySetResult();
                else await _earlierStepTried.Task.WaitAsync(TimeSpan.FromSeconds(1)).ContinueWith(_ => { });
            }

            lock (_gate)
            {
                if (step <= _lastAcceptedStep) return false;
                _lastAcceptedStep = step;
                return true;
            }
        });

        // The hash as Redis holds it: one live hash for the account; a DEL tells one caller it removed it.
        _hashes.GetAccountIdAsync(Hash).Returns(_ => { lock (_gate) return _hashLive ? _account.Id : null; });
        _hashes.RecordAttemptAsync(_account.Id).Returns(_ => { lock (_gate) return _hashLive ? 1L : -1L; });
        _hashes.TryConsumeAsync(Hash, _account.Id).Returns(_ =>
        {
            lock (_gate)
            {
                bool removed = _hashLive;
                _hashLive = false;
                return removed;
            }
        });
        _hashes.When(h => h.CleanupHash(Hash)).Do(_ => { lock (_gate) _hashLive = false; });
    }

    private MfaLoginPolicy Policy()
    {
        var mfa = new MFAService(NullLoggerFactory.Instance, _setups, _hashes, Substitute.For<ISecureRandom>(),
            Substitute.For<IReplicatedCache>());
        return TestLogin.Mfa(_accounts, _counters.Cache, mfa, _hashes);
    }

    private Task<MfaCodeAttempt> VerifyAsync(MfaLoginPolicy policy, string code) =>
        policy.CheckAsync(Hash, code, LoginSource.FromAddress(System.Net.IPAddress.Loopback), CancellationToken.None);

    private void NewHash()
    {
        lock (_gate) _hashLive = true;
    }

    /// <summary>
    /// Two codes the window accepts (this step's and the previous one's), sent together with one
    /// hash, both read the hash and the row before either writes. The earlier step is tried first,
    /// the order in which both used to pass the step check.
    /// </summary>
    [Fact]
    public async Task Let_only_one_of_two_parallel_verifies_of_one_hash_succeed()
    {
        var totp = new Totp(_secret);
        DateTime now = DateTime.UtcNow;
        string current = totp.ComputeTotp(now);
        string previous = totp.ComputeTotp(now.AddSeconds(-30));
        _earlierStep = (long)Math.Floor((now.AddSeconds(-30) - DateTime.UnixEpoch).TotalSeconds / 30);
        _holdReads = true;
        MfaLoginPolicy policy = Policy();

        MfaCodeAttempt[] results = await Task.WhenAll(
            Task.Run(() => VerifyAsync(policy, previous)),
            Task.Run(() => VerifyAsync(policy, current))).WaitAsync(Bound);

        Assert.Single(results, r => r.Result == MfaCodeCheck.Correct);
    }

    [Fact]
    public async Task Accept_a_code_one_step_from_now()
    {
        string code = new Totp(_secret).ComputeTotp(DateTime.UtcNow.AddSeconds(-30));

        MfaCodeAttempt result = await VerifyAsync(Policy(), code);

        Assert.Equal(MfaCodeCheck.Correct, result.Result);
    }

    /// <summary>Behind now on purpose: a code ahead would drift into the window if a step boundary passed mid-test.</summary>
    [Fact]
    public async Task Refuse_a_code_two_steps_from_now()
    {
        string code = new Totp(_secret).ComputeTotp(DateTime.UtcNow.AddSeconds(-60));

        MfaCodeAttempt result = await VerifyAsync(Policy(), code);

        Assert.Equal(MfaCodeCheck.WrongCode, result.Result);
    }

    /// <summary>
    /// A code accepted once is refused on the next login's fresh hash, as a replay (#478 review):
    /// the hash is not spent by it, and it is not a failed login.
    /// </summary>
    [Fact]
    public async Task Refuse_a_code_that_was_already_accepted_without_spending_the_hash()
    {
        string code = new Totp(_secret).ComputeTotp();
        MfaLoginPolicy policy = Policy();

        Assert.Equal(MfaCodeCheck.Correct, (await VerifyAsync(policy, code)).Result);
        NewHash();
        string usernameKey = Assert.Single(_counters.UsernameKeys);
        long before = _counters.CountOf(usernameKey);
        MfaCodeAttempt replay = await VerifyAsync(policy, code);

        Assert.Equal(MfaCodeCheck.Replayed, replay.Result);
        Assert.True(_hashLive);
        // Its own slot came back: the replay did not count.
        Assert.Equal(before, _counters.CountOf(usernameKey));
    }

    /// <summary>A right code spends its hash: it cannot be verified against again.</summary>
    [Fact]
    public async Task Spend_the_hash_when_a_code_is_accepted()
    {
        string code = new Totp(_secret).ComputeTotp();
        MfaLoginPolicy policy = Policy();

        Assert.Equal(MfaCodeCheck.Correct, (await VerifyAsync(policy, code)).Result);
        MfaCodeAttempt again = await VerifyAsync(policy, new Totp(_secret).ComputeTotp(DateTime.UtcNow.AddSeconds(30)));

        Assert.Equal(MfaCodeCheck.HashGone, again.Result);
    }
}
