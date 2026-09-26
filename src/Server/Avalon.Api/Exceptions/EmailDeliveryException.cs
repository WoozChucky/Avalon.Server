namespace Avalon.Api.Exceptions;

/// <summary>
/// An email the request depends on could not be sent (#510). Answered 503 "Email could not be
/// sent". It carries no inner exception on purpose: the sender's own exception can hold the body,
/// and the body can hold a token. The thrower logs what is safe to log.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public const string CouldNotSend = "Email could not be sent";

    public EmailDeliveryException() : base(CouldNotSend)
    {
    }
}
