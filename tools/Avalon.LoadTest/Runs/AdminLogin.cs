using System.Text;
using Avalon.LoadTest.Api;

namespace Avalon.LoadTest.Runs;

/// <summary>
/// The admin who provisions or deletes a run: the username from standard input, the password without echo. The
/// password is sent with each admin call (the endpoints ask for it again) and is never written anywhere.
/// </summary>
public sealed class AdminLogin
{
    private AdminLogin(string token, string password)
    {
        Token = token;
        Password = password;
    }

    /// <summary>The admin's access token.</summary>
    public string Token { get; }

    /// <summary>The admin's current password, which the load-test endpoints check again.</summary>
    public string Password { get; }

    /// <summary>Asks for the admin's username and password and signs in.</summary>
    public static async Task<AdminLogin> SignInAsync(ApiClient api, CancellationToken ct)
    {
        Console.Error.Write("Admin username: ");
        string user = Console.ReadLine()?.Trim() is { Length: > 0 } read
            ? read
            : throw new CommandLineException("No admin username was given.");
        Console.Error.Write($"Password for {user}: ");
        string password = ReadPassword();
        return new AdminLogin(await api.AdminTokenAsync(user, password, ct), password);
    }

    /// <summary>A line read without echo; piped input is read as it comes, as it shows nothing anyway.</summary>
    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? "";

        var password = new StringBuilder();
        for (ConsoleKeyInfo key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter;
             key = Console.ReadKey(intercept: true))
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return password.ToString();
    }
}
