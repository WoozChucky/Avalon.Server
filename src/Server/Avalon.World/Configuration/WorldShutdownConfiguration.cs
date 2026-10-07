using System.ComponentModel.DataAnnotations;
using Avalon.World.Maintenance;

namespace Avalon.World.Configuration;

/// <summary>
/// World:Shutdown (#768): how a stopping world warns and drains its players before the close and the saves, and how
/// long the host waits for all of it. Both are required: a missing section fails the start rather than guessing.
/// </summary>
public sealed class WorldShutdownConfiguration : IValidatableObject
{
    public const string Section = "World:Shutdown";

    /// <summary>
    /// The least <see cref="SaveMargin" />: the shutdown's own wait for saves, and the drain's wait past its deadline
    /// for a tick that does not end it. Any less, and a drain that runs to that wait leaves the saves no time.
    /// </summary>
    public static TimeSpan MinimumSaveMargin =>
        WorldServer.DefaultSaveDrainLimit + WorldMaintenanceCoordinator.RestartTickBackstop;

    /// <summary>
    /// How long a stop warns players of the restart and waits for them to leave before it closes them. The drain ends
    /// sooner once no non-Admin player is left. Zero stops as the world always did: everyone is closed at once.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan DrainTime { get; set; }

    /// <summary>
    /// What the close and the character saves get after the drain, at least <see cref="MinimumSaveMargin" />. The
    /// host's stop timeout is <see cref="DrainTime" /> plus this.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan SaveMargin { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SaveMargin < MinimumSaveMargin)
        {
            yield return new ValidationResult(
                $"SaveMargin must be at least {MinimumSaveMargin} (the shutdown's save wait and the drain's backstop)",
                [nameof(SaveMargin)]);
        }
    }
}
