namespace Avalon.Api.Distribution;

/// <summary>The manifest's exact bytes, its signature, and a presigned URL for each distinct blob.</summary>
public sealed record ManifestResponse(string Manifest, string Signature, IReadOnlyDictionary<string, Uri> Urls);
