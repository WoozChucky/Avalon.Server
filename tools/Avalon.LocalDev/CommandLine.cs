using System.Globalization;

namespace Avalon.LocalDev;

/// <summary>The options of <c>login</c> and <c>check</c>.</summary>
public sealed record CommandLine(string User, string? Password, Uri Api, string? Launch, ushort? World)
{
    public const string DefaultUser = "ADMIN";
    public const string DefaultApi = "https://localhost:7166";
    public const string PasswordVariable = "AVALON_DEV_PASSWORD";

    public static CommandLine Parse(string[] args)
    {
        string user = DefaultUser;
        string? password = null, launch = null, api = DefaultApi;
        ushort? world = null;
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (i + 1 == args.Length) throw new CommandLineException($"{option} needs a value.");
            string value = args[++i];
            switch (option)
            {
                case "--user": user = value; break;
                case "--password": password = value; break;
                case "--api": api = value; break;
                case "--launch": launch = value; break;
                case "--world":
                    world = ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort id) && id > 0
                        ? id
                        : throw new CommandLineException("--world takes a world id, 1 to 65535.");
                    break;
                default: throw new CommandLineException($"Unknown option {option}.");
            }
        }

        if (!Uri.TryCreate(api, UriKind.Absolute, out Uri? origin) || origin.Scheme != Uri.UriSchemeHttps)
            throw new CommandLineException("--api takes the API's https origin: the game routes refuse plain http.");

        // The routes are resolved against it, which keeps a path prefix (an ingress's /api) only behind a slash.
        if (!origin.AbsolutePath.EndsWith('/')) origin = new Uri(origin.AbsoluteUri + "/");
        return new(user, password, origin, launch, world);
    }
}

/// <summary>A command line the tool cannot run; the usage follows.</summary>
public sealed class CommandLineException(string message) : Exception(message);

/// <summary>A step that failed, with what to do about it; the tool prints it and exits with 1.</summary>
public sealed class LocalDevException(string message) : Exception(message);
