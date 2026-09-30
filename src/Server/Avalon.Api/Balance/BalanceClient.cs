using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalon.Balance.Contract;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Avalon.Api.Balance;

/// <summary>
/// A typed client of the in-cluster balance service. Only GETs are retried: a POST starts a run or opens
/// a pull request and a DELETE cancels one, so each goes out once. The shared secret is set on the
/// client's default headers at registration and appears nowhere else.
/// </summary>
public sealed class BalanceClient : IBalanceClient
{
    public const string SecretHeader = "X-Balance-Secret";
    internal const string UnavailableDetail = "balance service unavailable";
    internal const string RejectedDetail = "balance service rejected the API's credentials";

    private readonly HttpClient _http;

    public BalanceClient(HttpClient http)
    {
        _http = http;
    }

    public Task<BalanceResponse<CatalogDto>> CatalogAsync(CancellationToken ct) =>
        SendAsync<CatalogDto>(HttpMethod.Get, "catalog", null, ct);

    public Task<BalanceResponse<RunAcceptedDto>> StartRunAsync(RunRequestDto request, CancellationToken ct) =>
        SendAsync<RunAcceptedDto>(HttpMethod.Post, "runs", request, ct);

    public Task<BalanceResponse<RunStatusDto>> GetRunAsync(string id, CancellationToken ct) =>
        SendAsync<RunStatusDto>(HttpMethod.Get, $"runs/{Uri.EscapeDataString(id)}", null, ct);

    public async Task<BalanceResponse> CancelRunAsync(string id, CancellationToken ct) =>
        await SendAsync<object>(HttpMethod.Delete, $"runs/{Uri.EscapeDataString(id)}", null, ct);

    public Task<BalanceResponse<ExportResultDto>> ExportAsync(ExportRequestDto request, CancellationToken ct) =>
        SendAsync<ExportResultDto>(HttpMethod.Post, "exports", request, ct);

    private async Task<BalanceResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: BalanceJson.Options);
            using HttpResponseMessage response = await _http.SendAsync(request, ct);
            return await ReadAsync<T>(response, ct);
        }
        // After the retries a GET gets, or at once for a write: the service cannot be reached. The
        // inner exception stays for the log; the message names nothing.
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException ||
                                   (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new BalanceUnavailableException(UnavailableDetail, ex);
        }
    }

    private static async Task<BalanceResponse<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        int status = (int)response.StatusCode;
        // The service answers 401 only to a wrong secret: a fault of ours, not of the caller's session.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return new BalanceResponse<T>((int)HttpStatusCode.BadGateway, default, JsonSerializer.Serialize(new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadGateway,
                Title = "Bad gateway",
                Detail = RejectedDetail,
            }));

        bool isJson = response.Content.Headers.ContentType?.MediaType is { } type &&
                      (type == "application/json" || type.EndsWith("+json", StringComparison.Ordinal));
        string? json = isJson ? await response.Content.ReadAsStringAsync(ct) : null;
        if (string.IsNullOrWhiteSpace(json)) json = null;

        if (!response.IsSuccessStatusCode || json is null)
            return new BalanceResponse<T>(status, default, json);

        T? value;
        try
        {
            value = typeof(T) == typeof(object) ? default : JsonSerializer.Deserialize<T>(json, BalanceJson.Options);
        }
        catch (JsonException ex)
        {
            throw new BalanceUnavailableException(UnavailableDetail, ex);
        }
        return new BalanceResponse<T>(status, value, json);
    }
}

public static class BalanceClientRegistration
{
    /// <summary>The attempt timeout of a GET. A write may take longer: an export talks to GitHub.</summary>
    public static readonly TimeSpan GetAttemptTimeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan WriteAttemptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Registers <see cref="IBalanceClient" />. AddServiceDefaults gives every HttpClient the standard
    /// resilience handler, which retries every method; it is removed here and replaced with one that
    /// retries only GETs.
    /// </summary>
    /// <param name="retryDelay">The first retry's delay; tests shorten it.</param>
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental; the tests proving a POST is sent once guard it.
    public static IHttpClientBuilder AddBalanceClient(this IServiceCollection services, BalanceConfiguration config,
        TimeSpan? retryDelay = null)
    {
        IHttpClientBuilder http = services.AddHttpClient<IBalanceClient, BalanceClient>(client =>
            {
                client.BaseAddress = new Uri(config.Url.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Add(BalanceClient.SecretHeader, config.SharedSecret);
            })
            // The service gzips its answers, and a raw body is what the controller forwards.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            })
            .RemoveAllResilienceHandlers();
        http.AddResilienceHandler("balance", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = retryDelay ?? TimeSpan.FromMilliseconds(500),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    // The request comes from the context, not the outcome: a timeout or a refused
                    // connection has no response to read a method from.
                    ShouldHandle = args => ValueTask.FromResult(
                        IsGet(args.Context) && HttpClientResiliencePredicates.IsTransient(args.Outcome)),
                });
                builder.AddTimeout(new TimeoutStrategyOptions
                {
                    TimeoutGenerator = args => ValueTask.FromResult(IsGet(args.Context) ? GetAttemptTimeout : WriteAttemptTimeout),
                });
            });
        return http;
    }
#pragma warning restore EXTEXP0001

    private static bool IsGet(ResilienceContext context) => context.GetRequestMessage()?.Method == HttpMethod.Get;
}
