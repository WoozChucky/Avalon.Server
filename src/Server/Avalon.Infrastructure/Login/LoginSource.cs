using System.Net;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// Where a login attempt comes from: the key of its source's budget and the address recorded on
/// the account. An endpoint string and the REST API's <see cref="IPAddress"/> produce the same key.
/// An <see cref="Exempt"/> source, one the REST API names as a load machine, neither checks nor
/// spends that budget (<see cref="SourceBudget"/>).
/// </summary>
public readonly record struct LoginSource(string Key, string Ip, bool Exempt = false)
{
    public static LoginSource FromEndPoint(string remoteEndPoint) =>
        new(SourceBudget.KeyFor(remoteEndPoint), RemoteAddress.Of(remoteEndPoint));

    public static LoginSource FromAddress(IPAddress address, bool exempt = false) =>
        new(SourceBudget.KeyFor(address), RemoteAddress.Of(address), exempt);
}
