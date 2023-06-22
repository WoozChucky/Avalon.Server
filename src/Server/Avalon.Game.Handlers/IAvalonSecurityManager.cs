using Microsoft.Extensions.Logging;

namespace Avalon.Game.Handlers;

public interface IAvalonSecurityManager
{
    byte[] GetEncryptionKey();
}

public class AvalonSecurityManager : IAvalonSecurityManager
{
    private readonly ILogger<AvalonSecurityManager> _logger;

    public AvalonSecurityManager(ILogger<AvalonSecurityManager> logger)
    {
        _logger = logger;
    }

    public byte[] GetEncryptionKey()
    {
        _logger.LogDebug("Generating encryption key");

        var key = new byte[16];

        new Random().NextBytes(key);

        return key;
    }
}
