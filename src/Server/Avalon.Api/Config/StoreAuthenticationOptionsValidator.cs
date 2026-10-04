using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Config;

internal sealed class StoreAuthenticationOptionsValidator(IHostEnvironment? environment = null) : IValidateOptions<StoreAuthenticationConfiguration>
{
    public ValidateOptionsResult Validate(string? name, StoreAuthenticationConfiguration options)
    {
        try
        {
            options.Validate(environment is null || !environment.IsDevelopment());
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException error) { return ValidateOptionsResult.Fail(error.Message); }
    }
}
