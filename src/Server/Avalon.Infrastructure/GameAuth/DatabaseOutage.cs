using System.Data.Common;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// Tells a database outage (a transient provider error, the server unreachable, a timeout, the retry strategy giving
/// up) from a fault: a schema or SQL error, or a bug, is not an outage. Game authentication answers an outage as
/// unavailable, never as a refusal; a fault reaches the error handler.
/// </summary>
internal static class DatabaseOutage
{
    /// <summary>Whether <paramref name="error"/>, or any exception inside it, is a database outage.</summary>
    public static bool Is(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true } or TimeoutException or SocketException or IOException or RetryLimitExceededException)
                return true;
        }

        return false;
    }
}
