using Avalon.Domain.Commerce;

namespace Avalon.Api.Commerce;

public static class PurchaseErrors
{
    public const string LicenseAlreadyOwned = PurchaseFailureCodes.LicenseAlreadyOwned;
    public const string NeedsReview = PurchaseFailureCodes.NeedsReview;
    public const string NotFound = PurchaseFailureCodes.NotFound;
}

public sealed class PurchaseException(string code) : Exception("The purchase request could not be completed.")
{
    public string Code { get; } = code;
    public int StatusCode => Code switch
    {
        PurchaseFailureCodes.EmailNotVerified or PurchaseFailureCodes.AccountUnavailable => StatusCodes.Status403Forbidden,
        PurchaseFailureCodes.LicenseAlreadyOwned or PurchaseFailureCodes.NeedsReview => StatusCodes.Status409Conflict,
        PurchaseFailureCodes.NotFound => StatusCodes.Status404NotFound,
        PurchaseFailureCodes.Disabled => StatusCodes.Status501NotImplemented,
        PurchaseFailureCodes.TooManyAttempts => StatusCodes.Status429TooManyRequests,
        PurchaseFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
