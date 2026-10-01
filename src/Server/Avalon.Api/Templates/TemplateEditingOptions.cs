using Avalon.Domain.Auth;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Templates;

/// <summary>
/// The <c>Application:Templates</c> section: which worlds allow editing templates, and how long a save waits for
/// the world to confirm its reload. The values live in appsettings.json (and deployment overrides); nothing here
/// supplies one, so a missing <see cref="ReloadTimeout"/> is refused at startup.
/// </summary>
public sealed class TemplateEditingOptions
{
    public const string Section = "Application:Templates";

    /// <summary>The ids of the worlds whose templates can be edited. A world not listed is read-only.</summary>
    public List<ushort> EditableWorlds { get; set; } = [];

    /// <summary>How long a save waits for the world's reload result before answering "pending".</summary>
    public TimeSpan ReloadTimeout { get; set; }

    public bool IsEditable(WorldId world) => EditableWorlds.Contains(world.Value);
}

/// <summary>Refuses, at startup and naming the setting, a reload timeout that is not positive.</summary>
public sealed class TemplateEditingOptionsValidator : IValidateOptions<TemplateEditingOptions>
{
    public ValidateOptionsResult Validate(string? name, TemplateEditingOptions options) =>
        options.ReloadTimeout > TimeSpan.Zero
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{TemplateEditingOptions.Section}:ReloadTimeout must be a positive time span, e.g. 00:00:10.");
}

public static class TemplateEditingRegistration
{
    /// <summary>The template edit endpoints: the service, the guard in front of them, and the reload signal.</summary>
    public static IServiceCollection AddTemplateEditing(this IServiceCollection services)
    {
        services.AddScoped<TemplateEditService>();
        services.AddScoped<TemplateEditGuard>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITemplateReloadSignal, RedisTemplateReloadSignal>();
        return services;
    }
}
