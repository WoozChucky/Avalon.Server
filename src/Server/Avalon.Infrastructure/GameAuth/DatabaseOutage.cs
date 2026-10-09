using System.Data.Common;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// Tells a database outage (the server unreachable, a timeout, the retry strategy giving up) from a fault in the code.
/// Game authentication answers an outage as unavailable, never as a refusal; anything else is not an outage.
/// </summary>
internal static class DatabaseOutage
{
    /// <summary>Whether <paramref name="error"/>, or any exception inside it, is a database outage.</summary>
    public static bool Is(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is DbException or TimeoutException or SocketException or IOException or RetryLimitExceededException)
                return true;
        }

        return false;
    }
}
