// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Network.Packets.Auth;

namespace Avalon.Server.Auth.Telemetry;

/// <summary>
/// The <c>result</c> values of <c>avalon.auth.logins</c>: the answer the client got, except that a
/// refusal by the per-source or per-username budget is <see cref="RateLimited" />, though the
/// client is told LOCKED. An unknown username and a wrong password are one result, as they are on
/// the wire (#471).
/// </summary>
public static class LoginTelemetry
{
    public const string RateLimited = "rate_limited";

    public static string Tag(AuthResult result) => result switch
    {
        AuthResult.SUCCESS => "success",
        AuthResult.INVALID_CREDENTIALS => "invalid_credentials",
        AuthResult.LOCKED => "locked",
        AuthResult.MFA_REQUIRED => "mfa_required",
        AuthResult.ALREADY_CONNECTED => "already_connected",
        AuthResult.BANNED => "banned",
        AuthResult.DEACTIVATED => "deactivated",
        _ => "other",
    };
}
