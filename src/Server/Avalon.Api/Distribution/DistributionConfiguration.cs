namespace Avalon.Api.Distribution;

/// <summary>
/// The <c>Distribution</c> section: where game builds live (homelab Garage). Left empty, the
/// <c>/client</c> endpoints answer 503 and the rest of the API is unaffected.
/// </summary>
public sealed class DistributionConfiguration
{
    /// <summary>The S3 endpoint the API reads through, e.g. <c>http://garage.distribution.svc:3900</c>.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>The public URL presigned links point at, e.g. <c>https://dist.avalon.nunolevezinho.xyz</c>.</summary>
    public string PublicUrl { get; set; } = "";

    public string Bucket { get; set; } = "avalon-dist";

    public string Region { get; set; } = "garage";

    /// <summary>A read-only key: the API never writes to the bucket.</summary>
    public string AccessKeyId { get; set; } = "";

    public string SecretAccessKey { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(PublicUrl) &&
        !string.IsNullOrWhiteSpace(Bucket) && !string.IsNullOrWhiteSpace(AccessKeyId) &&
        !string.IsNullOrWhiteSpace(SecretAccessKey);
}
