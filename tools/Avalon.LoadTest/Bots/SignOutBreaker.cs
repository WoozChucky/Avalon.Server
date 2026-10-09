using Avalon.LoadTest.Api;

namespace Avalon.LoadTest.Bots;

/// <summary>How one sign-out through a <see cref="SignOutBreaker"/> ended.</summary>
/// <param name="Skipped">Not sent: the breaker had tripped.</param>
/// <param name="Failure">Why the sign-out failed, for a note; null when it was answered (or skipped).</param>
public readonly record struct SignOutOutcome(bool Skipped, string? Failure);

/// <summary>
/// The sign-outs of one ramp (the bots' leaves and the context refresher's), each on its own
/// <see cref="ApiClient.LogoutTimeout"/>, with a breaker: once <see cref="Threshold"/> sign-outs in a row end with no
/// reply, a 5xx or their own timeout, the API is taken as down and every sign-out after is skipped at once and counted
/// (<see cref="Skipped"/>), so the stop is not held up 10 s per bot for nothing; those contexts expire within 5
/// minutes. A sign-out answered before the breaker trips resets the count; once tripped it stays tripped. Thread-safe.
/// </summary>
public sealed class SignOutBreaker
{
    /// <summary>Failed sign-outs in a row that trip the breaker: one leave batch's worth.</summary>
    public const int Threshold = 32;

    private int _failuresInARow;
    private int _skipped;
    private volatile bool _tripped;

    /// <summary>Sign-outs skipped since the breaker tripped.</summary>
    public int Skipped => Volatile.Read(ref _skipped);

    /// <summary>Signs <paramref name="context"/> out on its own clock, unless the breaker has tripped; never throws.</summary>
    public async Task<SignOutOutcome> SignOutAsync(ApiClient api, GameContext context)
    {
        if (_tripped)
        {
            Interlocked.Increment(ref _skipped);
            return new SignOutOutcome(Skipped: true, Failure: null);
        }

        using var limit = new CancellationTokenSource(ApiClient.LogoutTimeout);
        try
        {
            await api.LogoutAsync(context, limit.Token);
            if (!_tripped) Interlocked.Exchange(ref _failuresInARow, 0);
            return new SignOutOutcome(Skipped: false, Failure: null);
        }
        catch (ApiException error)
        {
            // An answer other than a 5xx (a 400, a 429) says the API is up: neither a failure in a row nor a reset.
            if (error.Status is 0 or >= 500) Failed();
            return new SignOutOutcome(Skipped: false, Failure: error.Message);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            Failed();
            return new SignOutOutcome(Skipped: false,
                Failure: $"no reply within {ApiClient.LogoutTimeout.TotalSeconds:0} s");
        }
    }

    private void Failed()
    {
        if (Interlocked.Increment(ref _failuresInARow) >= Threshold) _tripped = true;
    }
}
