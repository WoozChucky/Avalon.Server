using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Worlds;

/// <summary>
/// The Database:Worlds check (#523) as an options validator: fails with the parser's refusal, which
/// names the setting and never its value. It reads the raw section, because Database:Worlds is a map
/// keyed by world id, not a DatabaseConfiguration property. Deliberately not registered with
/// ValidateOnStart: the OpenAPI document generator starts the host with no world configured, and the
/// host's startup validators would refuse it. The api's own startup parses the section explicitly.
/// </summary>
public sealed class WorldDatabasesValidation(IConfiguration configuration) : IValidateOptions<DatabaseConfiguration>
{
    public ValidateOptionsResult Validate(string? name, DatabaseConfiguration options) =>
        WorldDatabaseSettings.TryParse(configuration, out _, out string? refusal)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(refusal);
}
