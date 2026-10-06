using System.Reflection;
using Avalon.Api.Commerce;
using Avalon.Api.Controllers;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class PaymentNotificationShould
{
    [Fact]
    public async Task Signature_verified_before_inbox()
    {
        var repository = Substitute.For<IPurchaseRepository>();
        var provider = new NotificationProvider { Invalid = true };
        var service = Service(repository, provider);
        Assert.Equal(NotificationAcceptance.Invalid, await service.AcceptAsync("alternate", "payload"u8.ToArray(), new Dictionary<string, string>(), CancellationToken.None));
        await repository.DidNotReceiveWithAnyArgs().AcceptEventAsync(default!, default);
        provider.Invalid = false;
        Assert.Equal(NotificationAcceptance.Accepted, await service.AcceptAsync("alternate", "payload"u8.ToArray(), new Dictionary<string, string>(), CancellationToken.None));
        await repository.Received(1).AcceptEventAsync(Arg.Is<PaymentEvent>(x => x.Provider == "alternate" && x.ResourceKind == "checkout" && x.OrderId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task External_notification_cannot_request_an_internal_operation_replay()
    {
        var repository = Substitute.For<IPurchaseRepository>();
        var provider = new NotificationProvider { ResourceKind = "reconciliation" };
        Assert.Equal(NotificationAcceptance.Invalid, await Service(repository, provider).AcceptAsync("alternate", "payload"u8.ToArray(), new Dictionary<string, string>(), CancellationToken.None));
        await repository.DidNotReceiveWithAnyArgs().AcceptEventAsync(default!, default);
    }

    [Fact]
    public async Task Database_failure_never_acknowledges_event()
    {
        var repository = Substitute.For<IPurchaseRepository>();
        repository.AcceptEventAsync(Arg.Any<PaymentEvent>(), Arg.Any<CancellationToken>()).Returns<Task<bool>>(_ => throw new SqliteException("database unavailable", 5));
        await Assert.ThrowsAsync<SqliteException>(() => Service(repository, new()).AcceptAsync("alternate", "payload"u8.ToArray(), new Dictionary<string, string>(), CancellationToken.None));
    }

    [Fact]
    public async Task Duplicate_valid_event_is_acknowledged_and_oversized_body_never_parsed()
    {
        var repository = Substitute.For<IPurchaseRepository>();
        repository.AcceptEventAsync(Arg.Any<PaymentEvent>(), Arg.Any<CancellationToken>()).Returns(false);
        var provider = new NotificationProvider();
        var service = Service(repository, provider);
        Assert.Equal(NotificationAcceptance.Accepted, await service.AcceptAsync("alternate", "payload"u8.ToArray(), new Dictionary<string, string>(), CancellationToken.None));
        Assert.Equal(NotificationAcceptance.Invalid, await service.AcceptAsync("alternate", new byte[CommercePolicy.MaximumNotificationBytes + 1], new Dictionary<string, string>(), CancellationToken.None));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void Route_is_anonymous_bounded_and_does_not_use_browser_authority()
    {
        var type = typeof(PaymentNotificationsController);
        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("payments/notifications", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(type.GetCustomAttribute<RequestSizeLimitAttribute>());
    }

    private static PaymentNotificationService Service(IPurchaseRepository repository, NotificationProvider provider) => new(repository,
        new PaymentProviderRegistry([provider]), Options.Create(new CommerceConfiguration { Enabled = true, Provider = "alternate", ProviderAccountId = "merchant" }), TimeProvider.System);
    private sealed class NotificationProvider : IPaymentProvider
    {
        public string Provider => "alternate";
        public bool Invalid;
        public int Calls;
        public string ResourceKind = "checkout";
        public VerifiedPaymentNotification VerifyNotification(ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, DateTime now)
        {
            Calls++;
            if (Invalid) throw new PaymentProviderException("INVALID_NOTIFICATION");
            return new("alternate", "merchant", "sandbox", "event", "paid", ResourceKind, "checkout", null, null, null, now);
        }
        public Task<CheckoutProviderResult> CreateCheckoutAsync(CheckoutCreateCommand command, CancellationToken ct) => throw new NotSupportedException();
        public Task<PaymentSnapshot> GetCheckoutAsync(PaymentLookup lookup, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefundProviderResult> RequestFullRefundAsync(FullRefundCommand command, CancellationToken ct) => throw new NotSupportedException();
    }
}
