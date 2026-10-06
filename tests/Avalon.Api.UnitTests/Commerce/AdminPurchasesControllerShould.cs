using System.Reflection;
using System.Text.Json;
using Avalon.Api.Authentication;
using Avalon.Api.Contract.Commerce;
using Avalon.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class AdminPurchasesControllerShould
{
    [Fact]
    public void Player_cannot_search_refund_or_retry()
    {
        var type = typeof(AdminPurchasesController);
        Assert.Equal(AvalonRoles.Admin, type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("admin/purchases", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.All(type.GetMethods().Where(x => x.DeclaringType == type), method => Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    [Fact]
    public void Refund_request_rejects_amount_recipient_and_external_reference_fields()
    {
        var legitimate = "{\"paymentAttemptId\":\"11111111-1111-1111-1111-111111111111\",\"reason\":\"requested\"";
        foreach (var extra in new[] { "amountMinor", "accountId", "paymentReference" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FullRefundRequest>(legitimate + ",\"" + extra + "\":\"untrusted\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
