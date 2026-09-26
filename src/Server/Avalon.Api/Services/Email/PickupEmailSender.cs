using System.Globalization;
using System.Text;
using Avalon.Api.Config;

namespace Avalon.Api.Services.Email;

/// <summary>
/// The Development sender (#510): each email becomes one RFC 5322 .eml file in
/// <see cref="EmailConfig.PickupDirectory"/>, created when missing, which any mail client can open.
/// Nothing leaves the machine. Startup refuses it outside Development (<see cref="ServiceRegistration.AddEmail"/>),
/// since the files hold whatever the emails hold, confirm tokens included.
/// </summary>
public sealed class PickupEmailSender : IEmailSender
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly string _from;
    private readonly string _messageIdDomain;
    private readonly TimeProvider _time;

    public PickupEmailSender(EmailConfig config, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(config);
        _directory = config.PickupDirectory;
        _from = config.From ?? throw new ArgumentException("A pickup sender needs a From address.", nameof(config));
        _messageIdDomain = _from[(_from.LastIndexOf('@') + 1)..];
        _time = time;
    }

    public async Task SendAsync(string to, string subject, string textBody, CancellationToken ct)
    {
        // A line break in a header value would end the header and start another (Bcc, say).
        RefuseLineBreaks(to, nameof(to));
        RefuseLineBreaks(subject, nameof(subject));
        ArgumentNullException.ThrowIfNull(textBody);

        DateTimeOffset now = _time.GetUtcNow();
        Guid id = Guid.NewGuid();

        var eml = new StringBuilder();
        Header(eml, "From", _from);
        Header(eml, "To", to);
        Header(eml, "Subject", EncodeIfNeeded(subject));
        Header(eml, "Date", now.ToString("ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture));
        Header(eml, "Message-ID", $"<{id:N}@{_messageIdDomain}>");
        Header(eml, "MIME-Version", "1.0");
        Header(eml, "Content-Type", "text/plain; charset=utf-8");
        Header(eml, "Content-Transfer-Encoding", "8bit");
        eml.Append("\r\n");
        eml.Append(textBody.ReplaceLineEndings("\r\n"));
        if (!textBody.EndsWith('\n')) eml.Append("\r\n");

        // The files hold confirm tokens: on Unix, only the api's own user may open the folder.
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(_directory);
        else
            Directory.CreateDirectory(_directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string name = string.Create(CultureInfo.InvariantCulture, $"{now.UtcDateTime:yyyyMMddTHHmmssfff}-{id:N}.eml");
        string path = Path.Combine(_directory, name);
        // Written under another name and moved into place, so a reader watching the folder never
        // opens half an email.
        string partial = path + ".partial";
        await File.WriteAllTextAsync(partial, eml.ToString(), Utf8NoBom, ct);
        File.Move(partial, path);
    }

    private static void Header(StringBuilder eml, string name, string value) =>
        eml.Append(name).Append(": ").Append(value).Append("\r\n");

    private static void RefuseLineBreaks(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new ArgumentException("A header value cannot hold a line break.", name);
    }

    /// <summary>A header is ASCII; anything else goes as an RFC 2047 encoded word.</summary>
    private static string EncodeIfNeeded(string value) =>
        Ascii.IsValid(value) ? value : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
}
