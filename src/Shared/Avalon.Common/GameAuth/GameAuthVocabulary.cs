// Stable serialized vocabulary. Keep literal values compatible with native/browser contracts.
namespace Avalon.Common.GameAuth;

public static class GameAuthErrors
{
    /// <summary>The account is banned; named only after the caller proved who they are (#882).</summary>
    public const string AccountBanned = "ACCOUNT_BANNED";
    /// <summary>The account is being consolidated into another and plays again once that finishes (#882).</summary>
    public const string AccountConsolidating = "ACCOUNT_CONSOLIDATING";
    /// <summary>The account is deactivated; named only after the caller proved who they are (#882).</summary>
    public const string AccountDeactivated = "ACCOUNT_DEACTIVATED";
    public const string AccountLinkConflict = "ACCOUNT_LINK_CONFLICT";
    public const string AccountLinkRequired = "ACCOUNT_LINK_REQUIRED";
    public const string AccountMismatch = "ACCOUNT_MISMATCH";
    public const string AccountRequired = "ACCOUNT_REQUIRED";
    public const string AccountUnavailable = "ACCOUNT_UNAVAILABLE";
    public const string ActiveGameSession = "ACTIVE_GAME_SESSION";
    public const string AuthorityChanged = "AUTHORITY_CHANGED";
    public const string AuthorityRevoked = "AUTHORITY_REVOKED";
    public const string AuthorizationRequired = "AUTHORIZATION_REQUIRED";
    public const string BarrierPending = "BARRIER_PENDING";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
    public const string ConsolidationConfirmationRequired = "CONSOLIDATION_CONFIRMATION_REQUIRED";
    public const string ConsolidationNotRequired = "CONSOLIDATION_NOT_REQUIRED";
    public const string ContextChanged = "CONTEXT_CHANGED";
    public const string ContextRevoked = "CONTEXT_REVOKED";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    public const string FinalizationPending = "FINALIZATION_PENDING";
    public const string HttpsRequired = "HTTPS_REQUIRED";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    /// <summary>
    /// The request has no usable <c>Idempotency-Key</c> header: missing, empty, not a GUID, all zeros, or sent more than
    /// once. Answered 400 before anything else is read.
    /// </summary>
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdentityConflict = "IDENTITY_CONFLICT";
    public const string InvalidAdmission = "INVALID_ADMISSION";
    public const string InvalidAttempt = "INVALID_ATTEMPT";
    public const string InvalidConsolidation = "INVALID_CONSOLIDATION";
    public const string InvalidHandoff = "INVALID_HANDOFF";
    public const string InvalidLink = "INVALID_LINK";
    public const string InvalidProof = "INVALID_PROOF";
    public const string InvalidRefresh = "INVALID_REFRESH";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidTicket = "INVALID_TICKET";
    public const string InProgress = "IN_PROGRESS";
    public const string LinkAlreadyProposed = "LINK_ALREADY_PROPOSED";
    public const string LinkUnavailable = "LINK_UNAVAILABLE";
    public const string MaxCharacters = "MAX_CHARACTERS";
    public const string MfaInvalid = "MFA_INVALID";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string NameTaken = "NAME_TAKEN";
    public const string OwnershipRequired = "OWNERSHIP_REQUIRED";
    public const string ProofExpired = "PROOF_EXPIRED";
    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";
    public const string ReconnectUnavailable = "RECONNECT_UNAVAILABLE";
    public const string RefreshReuse = "REFRESH_REUSE";
    public const string RegistrationDetailsTaken = "REGISTRATION_DETAILS_TAKEN";
    public const string RegistrationLimit = "REGISTRATION_LIMIT";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string SessionConflict = "SESSION_CONFLICT";
    public const string SessionReplaced = "SESSION_REPLACED";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string SteamLinkChangedStartAgain = "STEAM_LINK_CHANGED_START_AGAIN";
    public const string TicketExpired = "TICKET_EXPIRED";
    public const string UnsupportedProtocol = "UNSUPPORTED_PROTOCOL";
    public const string WaitingForSession = "WAITING_FOR_SESSION";
    public const string WorkloadAuthenticationRequired = "WORKLOAD_AUTHENTICATION_REQUIRED";
    public const string WorldBarrierPending = "WORLD_BARRIER_PENDING";
    public const string WorldConfigIncomplete = "WORLD_CONFIG_INCOMPLETE";
    public const string WorldUnavailable = "WORLD_UNAVAILABLE";

    /// <summary>
    /// The game context may not enter the world (#1050, #1053): its application is not allowed on it, or its account's
    /// access level is below the world's, one answer for both so neither is told apart. A refusal no retry changes,
    /// unlike <see cref="WorldUnavailable" />, which means "try later". The REST join and reconnect tickets answer it
    /// 403; the redemption answers it to the world server, which refuses its client <c>AuthorizationRequired</c>.
    /// </summary>
    public const string WorldNotAllowed = "WORLD_NOT_ALLOWED";

    /// <summary>
    /// The client is older than the world accepts (#1053): the version its game context states is below the world's
    /// <c>MinVersion</c>. Permanent for this build: the REST join and reconnect tickets answer it 426 with the version
    /// required (<c>requiredVersion</c>), and a client shows "update required" rather than trying again.
    /// </summary>
    public const string ClientOutdated = "CLIENT_OUTDATED";

    /// <summary>
    /// The character asked for does not exist on the world or is not the account's (#1053), one answer for both.
    /// The REST join and reconnect tickets answer it 404.
    /// </summary>
    public const string CharacterNotFound = "CHARACTER_NOT_FOUND";

    /// <summary>
    /// Whether <paramref name="error"/> names the account's standing (banned, deactivated, consolidating), answered
    /// 403: the caller proved who they are and the account is refused.
    /// </summary>
    public static bool IsAccountStanding(string? error) => error is AccountBanned or AccountDeactivated or AccountConsolidating;
}

public static class GameAuthStates
{
    public const string PendingIdentity = "pending_identity";
    public const string PendingLicense = "pending_license";
    public const string PendingLink = "pending_link";
    public const string Authorized = "authorized";
    public const string Revoked = "revoked";
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Expired = "expired";
    public const string AwaitingGameConfirmation = "awaiting_game_confirmation";
    public const string Complete = "complete";
}

public static class StoreProviders
{
    public const string Steam = "steam";
    public const string Avalon = "avalon";
}

public static class GameLaunchChannels
{
    public const string Steam = StoreProviders.Steam;
    public const string Avalon = "avalon";
}

public static class GameAuthTokenKinds
{
    public const string Credential = "credential";
    public const string Refresh = "refresh";
}
