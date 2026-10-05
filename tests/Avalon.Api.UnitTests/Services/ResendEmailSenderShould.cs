using System.Net;
using System.Text;
using System.Text.Json;
using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.Exceptions;
using Avalon.Api.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public sealed class ResendEmailSenderShould
{
    private const string Secret = "test-only-resend-key";

    private static IEmailSender Sender(Handler handler)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Application:Email:Sender"] = "2",
            ["Application:Email:From"] = "noreply@avalon.nunolevezinho.xyz",
            ["Application:Email:FromName"] = "Avalon",
            ["Application:Email:ResendApiKey"] = Secret,
        };
        var config = ApiConfiguration.Bind(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()).Email!;
        var env = Substitute.For<IHostEnvironment>(); env.EnvironmentName.Returns("Production");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmail(config, env);
        services.AddHttpClient("ResendEmailSender").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    [Fact]
    public async Task SendsPlainTextWithConfiguredSender()
    {
        var handler = new Handler();
        await Sender(handler).SendAsync("player@example.test", "Verification", "Verify this address", default);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("https://api.resend.com/emails", handler.Uri);
        Assert.Equal("Bearer " + Secret, handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Avalon <noreply@avalon.nunolevezinho.xyz>", json.RootElement.GetProperty("from").GetString());
        Assert.Equal("player@example.test", json.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Verification", json.RootElement.GetProperty("subject").GetString());
        Assert.Equal("Verify this address", json.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(401)] [InlineData(429)] [InlineData(500)] [InlineData(302)]
    public async Task DoesNotLeakSecretsOnFailure(int status)
    {
        var handler = new Handler { Status = (HttpStatusCode)status, Response = "provider-secret " + Secret };
        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync("player@example.test", "Verify", "raw-token", default));
        Assert.DoesNotContain(Secret, ex.ToString());
        Assert.DoesNotContain("raw-token", ex.ToString());
        Assert.Null(ex.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"id\":\"\"}")] [InlineData("not-json")]
    public async Task RefusesMalformedSuccess(string response)
    {
        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(new Handler { Response = response }).SendAsync("player@example.test", "Verify", "token", default));
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(new Handler()).SendAsync("player@example.test", "Verify", "token", canceled.Token));
    }

    [Fact]
    public async Task SanitizesTransportTimeout()
    {
        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(new Handler { Failure = new TaskCanceledException("raw-token") }).SendAsync("player@example.test", "Verify", "token", default));
    }

    [Theory]
    [InlineData("player@example.test\r\nBcc: other@example.test", "Verify")]
    [InlineData("player@example.test", "Verify\nBcc: other@example.test")]
    public async Task RefusesHeaderInjection(string to, string subject)
    {
        var handler = new Handler();
        await Assert.ThrowsAsync<ArgumentException>(() => Sender(handler).SendAsync(to, subject, "token", default));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = "{\"id\":\"message-123\"}";
        public Exception? Failure;
        public int Calls;
        public string? Uri, Authorization, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri!.AbsoluteUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }
}
