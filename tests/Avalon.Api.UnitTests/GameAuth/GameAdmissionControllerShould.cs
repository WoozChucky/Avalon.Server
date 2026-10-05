using System.Reflection;
using System.Text.Json;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameAdmissionControllerShould
{
    [Fact]
    public void Reject_body_selected_account_session_fence_and_server_identity()
    {
        var credential = new string('A', 43);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameJoinRequest>("{\"GameContextCredential\":\"" + credential + "\",\"WorldId\":1,\"AccountId\":\"1\"}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JoinRedemptionRequest>("{\"JoinTicket\":\"" + credential + "\",\"ServerId\":\"world-other\"}"));
    }
    [Fact]
    public void Require_the_dedicated_workload_scheme_and_bound_noncacheable_requests()
    {
        var type = typeof(InternalGameAdmissionController);
        Assert.Equal(GameServerAuthHandler.Scheme, type.GetCustomAttribute<AuthorizeAttribute>()!.AuthenticationSchemes);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.Equal(4096, ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)type.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize);
    }
    [Fact]
    public async Task Reject_plain_http_before_ticket_creation_or_world_listing()
    {
        var h = new JoinHarness();
        var controller = new GameAdmissionController(h.Authorization, h.Tickets, h.Allocator, new GameApplicationAccessPolicy(Options.Create(h.Configuration))) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<BadRequestObjectResult>(await controller.Join(new GameJoinRequest { GameContextCredential = new string('A', 43), WorldId = 1 }, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Worlds(new GameContextCredentialRequest { GameContextCredential = new string('A', 43) }, CancellationToken.None));
        Assert.Empty(h.Store.Entries);
    }
}
