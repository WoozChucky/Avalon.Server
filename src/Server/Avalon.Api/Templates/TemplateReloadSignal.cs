namespace Avalon.Api.Templates;

/// <summary>
/// The part of a world's static data a template save changes. The names are the ones the reload request carries on
/// the wire, so the signal that sends it maps them to the world's own reload areas by name.
/// </summary>
public enum TemplateReloadArea
{
    Items,
    Abilities,
    Creatures,
}

/// <param name="Status"><c>applied</c>, <c>failed</c> or <c>pending</c> (the world did not answer in time).</param>
/// <param name="Summary">The world's own words on the outcome; null when it said none.</param>
public sealed record TemplateReloadResult(string Status, string? Summary)
{
    public const string Applied = "applied";
    public const string Failed = "failed";
    public const string Pending = "pending";
}

/// <summary>Asks a world to reload part of its static data after a template save, and reports how that went.</summary>
public interface ITemplateReloadSignal
{
    /// <summary>
    /// Returns once the world answers or the wait runs out. A save is already committed when this is called, so an
    /// outcome other than <c>applied</c> never undoes it.
    /// </summary>
    Task<TemplateReloadResult> RequestAsync(Avalon.Domain.Auth.WorldId world, TemplateReloadArea area, CancellationToken ct);
}

/// <summary>Asks no one: every save is <c>pending</c>. Stands in until the Redis-backed signal replaces it.</summary>
public sealed class NoopTemplateReloadSignal : ITemplateReloadSignal
{
    public Task<TemplateReloadResult> RequestAsync(Avalon.Domain.Auth.WorldId world, TemplateReloadArea area, CancellationToken ct) =>
        Task.FromResult(new TemplateReloadResult(TemplateReloadResult.Pending, null));
}
