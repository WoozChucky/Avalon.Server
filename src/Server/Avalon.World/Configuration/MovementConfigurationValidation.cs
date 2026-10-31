using Microsoft.Extensions.Options;

namespace Avalon.World.Configuration;

/// <summary>
/// Refuses <c>Game:Movement</c> (#1267) out of range, so a world never starts with an input catch-up shorter than one
/// step or longer than 2 seconds. The annotations on <see cref="GameConfiguration" /> do not reach a nested object,
/// hence this check.
/// </summary>
public sealed class MovementConfigurationValidation : IValidateOptions<GameConfiguration>
{
    public ValidateOptionsResult Validate(string? name, GameConfiguration options)
    {
        if (options.Movement is null)
            return ValidateOptionsResult.Fail("Game:Movement must be set");

        string? problem = options.Movement.Problem();
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }
}
