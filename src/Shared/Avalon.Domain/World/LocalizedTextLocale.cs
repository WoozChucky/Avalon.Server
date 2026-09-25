using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One translation of one <see cref="LocalizedText"/>. Composite key (TextId, Locale).</summary>
public class LocalizedTextLocale
{
    public LocalizedTextId TextId { get; set; } = default!;

    /// <summary>Reuses the enum accounts are stored with, so the two cannot drift.</summary>
    public AccountLocale Locale { get; set; }

    public string Text { get; set; } = string.Empty;
}
