using Avalon.Balance.Contract;

namespace Avalon.Balance.Service.Export;

/// <summary>The request is refused: nothing was sent to GitHub. Answers 422.</summary>
public sealed class ExportInvalidException(IReadOnlyList<IssueDto> issues) : Exception(issues.Count > 0 ? issues[0].Message : "invalid export")
{
    public IReadOnlyList<IssueDto> Issues { get; } = issues;
}
