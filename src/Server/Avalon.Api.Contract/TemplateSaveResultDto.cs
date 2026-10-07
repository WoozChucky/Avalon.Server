namespace Avalon.Api.Contract;

/// <summary>What a template save answers: the template as stored, and what happened when the world was asked to reload it.</summary>
/// <typeparam name="TTemplate">The template's read DTO, carrying the new version.</typeparam>
public sealed class TemplateSaveResultDto<TTemplate>
{
    /// <summary>The template as stored after the save, with its new version.</summary>
    public TTemplate Template { get; set; } = default!;

    public TemplateReloadDto Reload { get; set; } = new();
}

/// <summary>The world's answer to the reload a save asks for.</summary>
public sealed class TemplateReloadDto
{
    /// <summary>
    /// <c>applied</c>: the world reloaded the data. <c>failed</c>: the world refused it or could not be asked; the
    /// save is stored all the same. <c>pending</c>: the world did not answer in time; it may still apply it.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>The world's own words on the outcome; null when it said none.</summary>
    public string? Summary { get; set; }
}
