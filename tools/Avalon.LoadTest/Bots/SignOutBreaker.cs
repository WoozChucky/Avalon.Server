using Avalon.LoadTest.Api;

namespace Avalon.LoadTest.Bots;

/// <summary>How one sign-out through a <see cref="SignOutBreaker"/> ended.</summary>
/// <param name="Skipped">Not sent: the breaker had tripped.</param>
/// <param name="Failure">Why the sign-out failed, for a note; null when it was answered (or skipped).</param>
public readonly record struct SignOutOutcome(bool Skipped, string? Failure);

/// <summary>
/// The sign-outs of one ramp (the bots' leaves and the context refresher's), each on its own
/// <see cref="ApiClient.LogoutTimeout"/>, through a <see cref="Breaker"/>: once 32 sign-outs in a row end with no
/// reply, a 5xx or their own timeout, the API is taken as down and sign-outs are skipped at once and counted
/// (<see cref="Skipped"/>; those contexts expire within 5 minutes), but for one probe every 5 s, so the stop is not
/// held up 10 s per bot for nothing. An answered sign-out, a probe's included, closes the breaker again. Thread-safe.
/// </summary>
public sealed class SignOutBreaker
{
    private readonly Breaker _breaker = new();

    /// <summary>Sign-outs skipped while the breaker was tripped.</summary>
    public int Skipped => _breaker.Skipped;

    /// <summary>Signs <paramref name="context"/> out on its own clock, unless skipped; never throws.</summary>
    public async Task<SignOutOutcome> SignOutAsync(ApiClient api, GameContext context)
    {
        if (!_breaker.TryEnter()) return new SignOutOutcome(Skipped: true, Failure: null);

        using var limit = new CancellationTokenSource(ApiClient.LogoutTimeout);
        try
        {
            await api.LogoutAsync(context, limit.Token);
            _breaker.Succeeded();
            return new SignOutOutcome(Skipped: false, Failure: null);
        }
        catch (ApiException error)
        {
            // An answer other than a 5xx (a 400, a 429) says the API is up: neither a failure in a row nor a reset.
            if (error.Status is 0 or >= 500) _breaker.Failed();
            return new SignOutOutcome(Skipped: false, Failure: error.Message);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            _breaker.Failed();
            return new SignOutOutcome(Skipped: false,
                Failure: $"no reply within {ApiClient.LogoutTimeout.TotalSeconds:0} s");
        }
    }
}
