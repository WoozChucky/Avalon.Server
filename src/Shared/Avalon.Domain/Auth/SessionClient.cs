namespace Avalon.Domain.Auth;

/// <summary>
/// Which client a refresh-token family belongs to: the website's cookie session, or a launcher's
/// session, whose tokens travel in request and response bodies (#591). Each refresh endpoint accepts
/// only its own kind, so neither client can use, or end, the other's session.
/// </summary>
public enum SessionClient : byte
{
    Web = 0,
    Launcher = 1,
}
