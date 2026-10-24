using Microsoft.Extensions.Options;

namespace Avalon.World.Configuration;

/// <summary>
/// Refuses <c>Game:Items</c> (#1179) out of range, so a world never starts with rarity odds that cannot be drawn or a
/// sale that pays less than the sell price. The annotations on <see cref="GameConfiguration" /> do not reach a nested
/// object, hence this check.
/// </summary>
public sealed class ItemConfigurationValidation : IValidateOptions<GameConfiguration>
{
    public ValidateOptionsResult Validate(string? name, GameConfiguration options)
    {
        if (options.Items is null)
            return ValidateOptionsResult.Fail("Game:Items must be set");

        string? problem = options.Items.Problem();
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }
}
