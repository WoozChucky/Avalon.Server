using System.Diagnostics;
using System.Text.Json;

namespace Avalon.LocalDev;

/// <summary>The <c>dotnet</c> commands the setup runs: the user-secrets store and the development certificate check.</summary>
public static class Dotnet
{
    /// <summary>The user-secrets of the project in <paramref name="project"/>, flattened (<c>A:B</c> keys).</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ListSecretsAsync(string project,
        CancellationToken cancellationToken)
    {
        (int exitCode, string output, string error) =
            await RunAsync(["user-secrets", "list", "--json", "--project", project], null, cancellationToken);
        if (exitCode != 0) throw new LocalDevException($"dotnet user-secrets list failed for {project}: {error}");

        int begin = output.IndexOf("//BEGIN", StringComparison.Ordinal);
        int end = output.IndexOf("//END", StringComparison.Ordinal);
        if (begin < 0 || end < begin) return new Dictionary<string, string>(StringComparer.Ordinal);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(output[(begin + "//BEGIN".Length)..end])
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Adds or replaces <paramref name="secrets"/> in the project's user-secrets; the others are kept.</summary>
    public static async Task SetSecretsAsync(string project, IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken)
    {
        (int exitCode, _, string error) = await RunAsync(["user-secrets", "set", "--project", project],
            JsonSerializer.Serialize(secrets), cancellationToken);
        if (exitCode != 0) throw new LocalDevException($"dotnet user-secrets set failed for {project}: {error}");
    }

    /// <summary>Whether this machine trusts the ASP.NET Core development certificate the API's https endpoint uses.</summary>
    public static async Task<bool> DevelopmentCertificateTrustedAsync(CancellationToken cancellationToken) =>
        (await RunAsync(["dev-certs", "https", "--check", "--trust"], null, cancellationToken)).ExitCode == 0;

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IReadOnlyList<string> arguments,
        string? input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new LocalDevException("Could not start dotnet.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }
}
