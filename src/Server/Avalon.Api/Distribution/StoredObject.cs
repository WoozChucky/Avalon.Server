namespace Avalon.Api.Distribution;

/// <summary>A listed object: its key and when it was last written.</summary>
public sealed record StoredObject(string Key, DateTimeOffset Modified);
