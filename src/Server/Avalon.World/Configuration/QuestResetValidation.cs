using Microsoft.Extensions.Options;

namespace Avalon.World.Configuration;

/// <summary>
/// Refuses <c>Game:QuestResets</c> (#710) out of range, so a world never starts with a reset that cannot happen. The
/// annotations on <see cref="GameConfiguration" /> do not reach a nested object, hence this check.
/// </summary>
public sealed class QuestResetValidation : IValidateOptions<GameConfiguration>
{
    public ValidateOptionsResult Validate(string? name, GameConfiguration options)
    {
        if (options.QuestResets is null)
            return ValidateOptionsResult.Fail("Game:QuestResets must be set");

        string? problem = options.QuestResets.Problem();
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }
}
