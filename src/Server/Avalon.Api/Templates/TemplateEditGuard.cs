using Avalon.Api.Hosting.Worlds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Templates;

/// <summary>
/// The first two answers of a template save, decided before the body is read and before the database is touched:
/// 403 for a world that is not listed in <c>Application:Templates:EditableWorlds</c>, then 428 for a request with no
/// <c>If-Match</c>. Authorization has already run, so only an Admin gets here, and Admin rights do not skip the
/// first check. A resource filter, because those run ahead of model binding.
/// </summary>
public sealed class TemplateEditGuard(ICurrentWorld world, IOptions<TemplateEditingOptions> options) : IAsyncResourceFilter
{
    public const string NotEditableTitle = "This world is not editable";

    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (world.Id is not { } id || !options.Value.IsEditable(id))
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, NotEditableTitle,
                "Templates on this world are read-only.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(context.HttpContext.Request.Headers.IfMatch.ToString()))
        {
            context.Result = Problem(StatusCodes.Status428PreconditionRequired, "Version required",
                "Send the version you read as If-Match.");
            return Task.CompletedTask;
        }

        return next();
    }

    internal static ObjectResult Problem(int status, string title, string detail) => new(new ProblemDetails
    {
        Status = status,
        Title = title,
        Detail = detail,
    })
    {
        StatusCode = status,
        ContentTypes = { "application/problem+json" },
    };
}

/// <summary>Reads an <c>If-Match</c> header against a row's version.</summary>
public static class TemplateIfMatch
{
    /// <summary>True when one of the header's entity tags is exactly the version (quoted or not). <c>*</c> never matches.</summary>
    public static bool Matches(string header, string version)
    {
        foreach (string part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part.Trim('"'), version, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

/// <summary>The answers a template save gives from the controller, with the guard's wording.</summary>
public static class TemplateProblems
{
    public const string ChangedTitle = "Changed since you opened it";

    public static ObjectResult Conflict() => TemplateEditGuard.Problem(StatusCodes.Status409Conflict, ChangedTitle,
        "Someone saved this template after you read it. Reload it and apply your change again.");
}
