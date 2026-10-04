using System.Security.Claims;
using System.Text.Json;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameSessionControlShould
{
    [Fact]
    public void Reject_body_selected_workload_identity()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameSessionControlRequest>(
            "{\"AccountId\":\"7\",\"GameSessionId\":\"" + Guid.NewGuid() + "\",\"FencingToken\":\"1\",\"ServerId\":\"another-world\"}"));
    }

    [Theory]
    [InlineData(false, true, "7", "1")]
    [InlineData(true, false, "7", "1")]
    [InlineData(true, true, "07", "1")]
    [InlineData(true, true, "7", "01")]
    [InlineData(true, true, "0", "1")]
    [InlineData(true, true, "7", "-1")]
    public async Task Deny_untrusted_transport_and_noncanonical_identifiers_before_repository_access(bool https, bool server, string account, string fence)
    {
        var h = new JoinHarness();
        var sessions = Substitute.For<IGameSessionRepository>();
        var service = new GameSessionFenceService(sessions, h.Authorization, Substitute.For<IWorldRepositories>(),
            Substitute.For<IAccountRepository>(), Options.Create(new GameWorkloadConfiguration()), h.Clock,
            new Avalon.Infrastructure.GameAuth.GameApplicationAccessPolicy(Options.Create(h.Configuration)));
        var http = new DefaultHttpContext();
        http.Request.Scheme = https ? "https" : "http";
        if (server) http.User = new(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "world-1")], GameServerAuthHandler.Scheme));
        var controller = new InternalGameAdmissionController(h.Tickets) { ControllerContext = new() { HttpContext = http } };
        var request = new GameSessionControlRequest { AccountId = account, FencingToken = fence, GameSessionId = Guid.NewGuid() };
        foreach (var result in new[] {
            await controller.Activate(request, service, CancellationToken.None),
            await controller.Heartbeat(request, service, CancellationToken.None),
            await controller.End(request, service, CancellationToken.None) })
            Assert.True(result is BadRequestObjectResult or UnauthorizedObjectResult);
        Assert.Empty(sessions.ReceivedCalls());
    }
}
