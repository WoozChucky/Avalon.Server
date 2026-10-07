namespace Avalon.Api.Distribution;

/// <summary>A manifest as stored: the parsed document, its exact bytes and its Ed25519 signature.</summary>
/// <remarks>
/// The raw JSON is handed back unchanged: the launcher verifies the signature over these bytes, so
/// a re-serialised copy would fail verification.
/// </remarks>
public sealed record StoredManifest(ManifestDocument Document, string Json, string Signature);
