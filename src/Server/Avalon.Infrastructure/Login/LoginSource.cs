using System.Net;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// Where a login attempt comes from: the key of its source's budget and the address recorded on
/// the account. Built from a TCP endpoint string or from the REST API's <see cref="IPAddress"/>;
/// one address gives one key either way, so a source guessing through both servers spends one
/// budget.
/// </summary>
public readonly record struct LoginSource(string Key, string Ip)
{
    public static LoginSource FromEndPoint(string remoteEndPoint) =>
        new(SourceBudget.KeyFor(remoteEndPoint), RemoteAddress.Of(remoteEndPoint));

    public static LoginSource FromAddress(IPAddress address) =>
        new(SourceBudget.KeyFor(address), RemoteAddress.Of(address));
}
