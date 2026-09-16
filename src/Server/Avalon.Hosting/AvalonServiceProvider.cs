using System;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Hosting;

/// <summary>
/// The container settings every Avalon host builds with. Validation is unconditional, including
/// in Production: a singleton that captures a scoped service is a defect wherever it runs, and
/// the one this guards against — packet handlers built from the root provider holding a
/// <c>DbContext</c> for the life of the process — was silent precisely because Production
/// leaves the checks off by default.
/// </summary>
public static class AvalonServiceProvider
{
    public static void Configure(ServiceProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    }

    public static ServiceProviderOptions Options
    {
        get
        {
            ServiceProviderOptions options = new();
            Configure(options);
            return options;
        }
    }
}
