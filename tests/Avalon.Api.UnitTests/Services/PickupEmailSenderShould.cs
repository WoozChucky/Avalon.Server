using Avalon.Api.Config;
using Avalon.Api.Services.Email;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #510: the Development sender writes each email as an RFC 5322 .eml file into a pickup folder,
/// and refuses a line break in a header value, so a value cannot add headers of its own.
/// </summary>
public sealed class PickupEmailSenderShould : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "avalon-mail-test-" + Guid.NewGuid().ToString("N"), "nested");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 26, 14, 3, 4, 567, TimeSpan.Zero));

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_directory)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private PickupEmailSender Sender() => new(new EmailConfig
    {
        Sender = EmailSenderKind.Pickup, PickupDirectory = _directory, From = "noreply@avalon.monster",
    }, _time);

    private static (Dictionary<string, string> Headers, string Body) Parse(string eml)
    {
        int split = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(split > 0, "No blank line between the headers and the body.");
        var headers = eml[..split].Split("\r\n")
            .Select(line => line.Split(": ", 2))
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        return (headers, eml[(split + 4)..]);
    }

    [Fact]
    public async Task Write_one_eml_file_with_the_headers_and_the_body_creating_the_directory()
    {
        Assert.False(Directory.Exists(_directory));

        await Sender().SendAsync("player@avalon.monster", "Confirm your new email", "Line one\nLine two",
            CancellationToken.None);

        string file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal(".eml", Path.GetExtension(file));
        Assert.StartsWith("20260926T140304567-", Path.GetFileName(file), StringComparison.Ordinal);
        (Dictionary<string, string> headers, string body) = Parse(await File.ReadAllTextAsync(file));
        Assert.Equal("noreply@avalon.monster", headers["From"]);
        Assert.Equal("player@avalon.monster", headers["To"]);
        Assert.Equal("Confirm your new email", headers["Subject"]);
        Assert.Equal("Sat, 26 Sep 2026 14:03:04 +0000", headers["Date"]);
        Assert.Matches("^<[0-9a-f]{32}@avalon\\.monster>$", headers["Message-ID"]);
        Assert.Equal("1.0", headers["MIME-Version"]);
        Assert.Equal("text/plain; charset=utf-8", headers["Content-Type"]);
        Assert.Equal("Line one\r\nLine two\r\n", body);
    }

    /// <summary>#510 review: the files hold confirm tokens, so on Unix only the api's own user may open the folder.</summary>
    [Fact]
    public async Task Create_the_directory_readable_by_its_owner_only_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;

        await Sender().SendAsync("player@avalon.monster", "Subject", "body", CancellationToken.None);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_directory));
    }

    [Fact]
    public async Task Give_every_email_its_own_file()
    {
        await Sender().SendAsync("a@avalon.monster", "One", "1", CancellationToken.None);
        await Sender().SendAsync("b@avalon.monster", "Two", "2", CancellationToken.None);

        Assert.Equal(2, Directory.GetFiles(_directory, "*.eml").Length);
    }

    [Theory]
    [InlineData("player@avalon.monster\r\nBcc: thief@avalon.monster", "Subject")]
    [InlineData("player@avalon.monster\n", "Subject")]
    [InlineData("player@avalon.monster", "Subject\r\nBcc: thief@avalon.monster")]
    [InlineData("player@avalon.monster", "Subject\rX: y")]
    public async Task Refuse_a_line_break_in_the_recipient_or_the_subject(string to, string subject)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Sender().SendAsync(to, subject, "body", CancellationToken.None));

        Assert.False(Directory.Exists(_directory) && Directory.GetFiles(_directory).Length > 0);
    }
}
