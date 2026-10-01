using Microsoft.Extensions.Logging;

namespace Avalon.World.Quests;

/// <summary>
/// The provider every QuestScript is built from (#738): over the world's container, it hands over a logger factory,
/// loggers (<c>ILogger&lt;T&gt;</c>) and the clock (<c>TimeProvider</c>), and answers null for everything else,
/// the provider itself included. So a script's constructor cannot obtain QuestService, IWorld or any other service that
/// writes, and "a script's one write is IQuestContext.Advance" is enforced rather than a convention. A constructor
/// asking for anything else cannot be built: QuestService logs it at Error and the quest cannot be accepted.
/// Stateless apart from the container it wraps.
/// </summary>
public sealed class QuestScriptServices(IServiceProvider services) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return IsAllowed(serviceType) ? services.GetService(serviceType) : null;
    }

    private static bool IsAllowed(Type type) =>
        type == typeof(ILoggerFactory)
        || type == typeof(TimeProvider)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>));
}
