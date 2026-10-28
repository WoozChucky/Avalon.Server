using Microsoft.Extensions.Options;

namespace Avalon.World.Configuration;

/// <summary>
/// Refuses <c>Game:Talents</c> (#1181 Part C) out of range, so a world never starts with a refund price setting (the
/// base price or the price per level) above the gold cap. The annotations on <see cref="GameConfiguration" /> do not
/// reach a nested object, hence this check.
/// </summary>
public sealed class TalentConfigurationValidation : IValidateOptions<GameConfiguration>
{
    public ValidateOptionsResult Validate(string? name, GameConfiguration options)
    {
        if (options.Talents is null)
            return ValidateOptionsResult.Fail("Game:Talents must be set");

        string? problem = options.Talents.Problem(options.MaxGold);
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }
}
