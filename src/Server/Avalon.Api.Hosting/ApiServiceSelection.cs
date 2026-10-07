namespace Avalon.Api.Hosting;

/// <summary>
/// The services an API process runs (#794, design section 2.2): those <see cref="Setting"/> names, compared without
/// case and taken in the host's order, or every service of the host when the setting is absent. An empty list, or a
/// name the host has no service for, stops startup, naming the setting.
/// </summary>
public sealed class ApiServiceSelection
{
    public const string Setting = "Application:Services";

    /// <summary>The OpenTelemetry resource attribute that names the services a process runs.</summary>
    public const string ResourceAttribute = "avalon.api.services";

    private ApiServiceSelection(IReadOnlyList<IApiService> services) => Services = services;

    /// <summary>The services this process runs, in the host's order.</summary>
    public IReadOnlyList<IApiService> Services { get; }

    /// <summary>Their names, in the host's order, comma-separated.</summary>
    public string Names => string.Join(",", Services.Select(service => service.Name));

    /// <summary>The services of <paramref name="available"/> that <paramref name="configuration"/> names under <see cref="Setting"/>.</summary>
    public static ApiServiceSelection From(IConfiguration configuration, IReadOnlyList<IApiService> available)
    {
        IConfigurationSection section = configuration.GetSection(Setting);
        string?[] names = section.GetChildren().Select(entry => entry.Value).ToArray();
        if (names.Length == 0 && !string.IsNullOrEmpty(section.Value))
            names = [section.Value];

        if (names.Length == 0)
        {
            if (!IsSet(configuration, section))
                return new ApiServiceSelection(available);

            throw new InvalidOperationException(
                $"{Setting} lists no service. Name one or more of {Known(available)}, or leave it unset to run them all.");
        }

        string[] unknown = names
            .Where(name => !available.Any(service => string.Equals(service.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Select(name => $"'{name}'")
            .ToArray();
        if (unknown.Length != 0)
        {
            throw new InvalidOperationException(
                $"{Setting} names {string.Join(", ", unknown)}, which this host does not run. Its services are {Known(available)}.");
        }

        return new ApiServiceSelection(available
            .Where(service => names.Contains(service.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray());
    }

    /// <summary>
    /// Whether the setting is there at all: an empty JSON array leaves its key with no value, and an environment
    /// variable set to nothing gives it an empty one, while an absent setting has no key in any source.
    /// </summary>
    private static bool IsSet(IConfiguration configuration, IConfigurationSection section) =>
        section.Value is not null
        || (configuration is IConfigurationRoot root && root.Providers.Any(provider => provider.TryGet(Setting, out _)));

    private static string Known(IReadOnlyList<IApiService> available) =>
        string.Join(", ", available.Select(service => service.Name));
}
