using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// #510: <c>Application:Email</c> decides whether the api can send email, and therefore whether
/// email change is on. <c>None</c>, the default, registers no sender. <c>Pickup</c> writes files on
/// the api's own disk, which only a developer can read, so startup refuses it outside Development,
/// and refuses it without a valid <c>From</c>.
/// </summary>
public class EmailSenderRegistrationShould
{
    private static IHostEnvironment Environment(string name)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    private static EmailConfig Bind(Dictionary<string, string?> settings) =>
        ApiConfiguration.Bind(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()).Email
        ?? new EmailConfig();

    private static IEmailSender? Registered(EmailConfig? config, string environment)
    {
        ServiceCollection services = new();
        services.AddEmail(config, Environment(environment));
        return services.BuildServiceProvider().GetService<IEmailSender>();
    }

    [Fact]
    public void Default_to_no_sender_with_the_pickup_directory_under_local_application_data()
    {
        EmailConfig config = Bind([]);

        Assert.Equal(EmailSenderKind.None, config.Sender);
        Assert.Equal(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "avalon-mail"),
            config.PickupDirectory);
        Assert.Null(Registered(config, Environments.Development));
        Assert.Null(Registered(null, Environments.Production));
    }

    [Fact]
    public void Bind_the_sender_the_directory_and_the_from_address()
    {
        EmailConfig config = Bind(new()
        {
            ["Application:Email:Sender"] = "Pickup",
            ["Application:Email:PickupDirectory"] = "/tmp/mail",
            ["Application:Email:From"] = "noreply@avalon.monster",
        });

        Assert.Equal(EmailSenderKind.Pickup, config.Sender);
        Assert.Equal("/tmp/mail", config.PickupDirectory);
        Assert.Equal("noreply@avalon.monster", config.From);
    }

    [Fact]
    public void Register_the_pickup_sender_in_development()
    {
        var config = new EmailConfig { Sender = EmailSenderKind.Pickup, From = "noreply@avalon.monster" };

        Assert.IsType<PickupEmailSender>(Registered(config, Environments.Development));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Refuse_the_pickup_sender_outside_development_naming_the_setting(string environment)
    {
        var config = new EmailConfig { Sender = EmailSenderKind.Pickup, From = "noreply@avalon.monster" };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddEmail(config, Environment(environment)));

        Assert.Contains("Application:Email:Sender", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData(" noreply@avalon.monster")]
    [InlineData("noreply@avalon.monster\r\nBcc: thief@avalon.monster")]
    [InlineData("Avalon <noreply@avalon.monster>")]
    public void Refuse_the_pickup_sender_without_a_valid_from_address(string? from)
    {
        var config = new EmailConfig { Sender = EmailSenderKind.Pickup, From = from };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddEmail(config, Environment(Environments.Development)));

        Assert.Contains("Application:Email:From", refused.Message, StringComparison.Ordinal);
    }

    // ---------------- The email-change send budgets (#510 review) ----------------

    [Fact]
    public void Default_the_send_budgets_to_three_per_account_and_per_address_an_hour()
    {
        var config = new AuthenticationConfig();

        Assert.Equal(3, config.MaxEmailChangeSendsPerAccount);
        Assert.Equal(3, config.MaxEmailChangeSendsPerAddress);
        Assert.Equal(60, config.EmailChangeSendWindowMinutes);
        ServiceRegistration.ValidateEmailChangeSendCaps(config);
    }

    [Theory]
    [InlineData(nameof(AuthenticationConfig.MaxEmailChangeSendsPerAccount))]
    [InlineData(nameof(AuthenticationConfig.MaxEmailChangeSendsPerAddress))]
    [InlineData(nameof(AuthenticationConfig.EmailChangeSendWindowMinutes))]
    public void Refuse_a_send_budget_below_one_naming_the_setting(string setting)
    {
        var config = new AuthenticationConfig();
        typeof(AuthenticationConfig).GetProperty(setting)!.SetValue(config, 0);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            ServiceRegistration.ValidateEmailChangeSendCaps(config));

        Assert.Contains("Application:Authentication:" + setting, refused.Message, StringComparison.Ordinal);
    }
}
