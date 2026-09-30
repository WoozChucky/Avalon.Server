using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class ServiceHostShould
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Answer_health_without_the_secret(string path)
    {
        await using WebApplication app = BalanceTestHost.Build();
        await app.StartAsync();

        HttpResponseMessage response = await app.GetTestClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData("0123456789abcdef0123456789abcdef-secreT")]
    public async Task Refuse_a_wrong_secret_without_detail(string? header)
    {
        await using WebApplication app = BalanceTestHost.Build();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();
        if (header is not null)
            client.DefaultRequestHeaders.Add("X-Balance-Secret", header);

        HttpResponseMessage response = await client.GetAsync("/catalog");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("", body);
        Assert.DoesNotContain(response.Headers.SelectMany(h => h.Value), v => v.Contains(BalanceTestHost.Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-secret")]
    public async Task Refuse_to_start_without_a_long_enough_secret(string? secret)
    {
        await using WebApplication app = BalanceTestHost.Build(secret);

        OptionsValidationException ex = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());

        Assert.Contains("Balance:SharedSecret", ex.Message);
    }
}
