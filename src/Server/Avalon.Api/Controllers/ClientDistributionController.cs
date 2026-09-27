using Avalon.Api.Authentication;
using Avalon.Api.Distribution;
using Avalon.Common.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

/// <summary>
/// Game distribution: the launcher installer (public), its update feed (public), recent builds'
/// notes (public for live, more channels for a signed-in account), and a channel's manifest with
/// presigned download URLs. A channel the caller may not use reads as not found, like an unknown one.
/// </summary>
[ApiController]
[Route("client")]
public class ClientDistributionController : BaseController
{
    private const int MaxReleases = 50;
    private readonly ClientDistributionService _service;

    public ClientDistributionController(ClientDistributionService service)
    {
        _service = service;
    }

    /// <summary>The latest launcher installer: version, size, SHA-256 and a download URL (15 min).</summary>
    [HttpGet("launcher", Name = "GetLauncher")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LauncherDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Launcher(CancellationToken ct) =>
        await _service.GetLauncherAsync(ct) is { } launcher ? Ok(launcher) : NotFound();

    /// <summary>The launcher's self-update feed, in Tauri's updater format.</summary>
    [HttpGet("launcher/update", Name = "GetLauncherUpdate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TauriUpdateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> LauncherUpdate(CancellationToken ct) =>
        await _service.GetLauncherUpdateAsync(ct) is { } update ? Ok(update) : NotFound();

    /// <summary>Recent builds, newest first: live for everyone, plus the caller's other channels.</summary>
    [HttpGet("releases", Name = "ListClientReleases")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<ReleaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Releases([FromQuery] int limit = 10, CancellationToken ct = default)
    {
        AccountAccessLevel? caller = User.Identity?.IsAuthenticated == true ? User.AccessLevel() : null;
        return Ok(await _service.ListReleasesAsync(caller, Math.Clamp(limit, 1, MaxReleases), ct));
    }

    /// <summary>
    /// The public changelog, newest first: server and launcher releases for everyone, game client builds
    /// for the channels the caller may use (homelab spec 2026-09-27-avalon-changelog-design §7). Page with
    /// <paramref name="before" /> and <paramref name="beforeId" />: the last entry's <c>publishedAt</c> and
    /// <c>id</c> on the previous page.
    /// </summary>
    [HttpGet("changelog", Name = "ListClientChangelog")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<ChangelogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Changelog([FromQuery] string? product = null, [FromQuery] string? channel = null,
        [FromQuery] int limit = 20, [FromQuery] DateTimeOffset? before = null, [FromQuery] string? beforeId = null,
        CancellationToken ct = default)
    {
        if (product is not (null or "server" or "client" or "launcher"))
            return BadRequest();
        Channel? parsed = null;
        if (channel is not null)
        {
            // Only game client builds have channels.
            if (product != "client" || !ChannelNames.TryParse(channel, out Channel c))
                return BadRequest();
            parsed = c;
        }

        AccountAccessLevel? caller = User.Identity?.IsAuthenticated == true ? User.AccessLevel() : null;
        var query = new ChangelogQuery(product, parsed, Math.Clamp(limit, 1, MaxReleases), before, beforeId);
        return Ok(await _service.ListChangelogAsync(caller, query, ct));
    }

    /// <summary>The channels the caller may use, each with its current build.</summary>
    [HttpGet("channels", Name = "ListClientChannels")]
    [Authorize(Policy = AvalonRoles.Player)]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Channels(CancellationToken ct) =>
        Ok(await _service.ListChannelsAsync(User.AccessLevel(), ct));

    /// <summary>
    /// The channel's current manifest (exact bytes), its Ed25519 signature, and a presigned URL per
    /// blob (1 h). 404 for a channel that is unknown, unpublished, or not the caller's to use.
    /// </summary>
    [HttpGet("channels/{channel}/manifest", Name = "GetClientManifest")]
    [Authorize(Policy = AvalonRoles.Player)]
    [ProducesResponseType(typeof(ManifestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Manifest([FromRoute] string channel, CancellationToken ct)
    {
        if (!ChannelNames.TryParse(channel, out Channel parsed))
            return NotFound();
        return await _service.GetManifestAsync(parsed, User.AccessLevel(), ct) is { } manifest ? Ok(manifest) : NotFound();
    }
}
