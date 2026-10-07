using System.Reflection;
using Avalon.Api.Commerce.Controllers;
using Avalon.Api.Hosting.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Avalon.Api.Commerce.UnitTests;

public sealed class AccountPurchasesControllerShould
{
    [Fact]
    public void Require_player_authority_and_no_cache()
    {
        Type type = typeof(AccountPurchasesController);
        Assert.Equal(AvalonRoles.Player, type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.Equal("account", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Checkout_accepts_no_browser_purchase_or_beneficiary_fields()
    {
        MethodInfo method = typeof(AccountPurchasesController).GetMethod("Checkout")!;
        Assert.Empty(method.GetParameters());
        Assert.Equal("CreateAccountPurchaseCheckout", method.GetCustomAttribute<HttpPostAttribute>()!.Name);
        Assert.Equal("purchases/checkout", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }
}
