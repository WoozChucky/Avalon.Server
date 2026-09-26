using System.Collections.Concurrent;
using Avalon.Api.Services.Email;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// An email sender that keeps what it is given (#510). A send to an address <see cref="FailFor"/>
/// returns true for throws instead, with the body in the exception's message, so a caller that
/// logs the exception would log the token; it is kept in <see cref="Refused"/>.
/// </summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    public sealed record Sent(string To, string Subject, string TextBody);

    private readonly ConcurrentQueue<Sent> _sent = new();
    private readonly ConcurrentQueue<Sent> _refused = new();

    public IReadOnlyList<Sent> All => _sent.ToList();

    public IReadOnlyList<Sent> Refused => _refused.ToList();

    public Func<string, bool> FailFor { get; set; } = _ => false;

    public Task SendAsync(string to, string subject, string textBody, CancellationToken ct)
    {
        if (FailFor(to))
        {
            _refused.Enqueue(new Sent(to, subject, textBody));
            throw new InvalidOperationException($"The test sender refused the email to {to}: {textBody}");
        }
        _sent.Enqueue(new Sent(to, subject, textBody));
        return Task.CompletedTask;
    }

    public IReadOnlyList<Sent> To(string address) =>
        _sent.Where(s => string.Equals(s.To, address, StringComparison.Ordinal)).ToList();
}
