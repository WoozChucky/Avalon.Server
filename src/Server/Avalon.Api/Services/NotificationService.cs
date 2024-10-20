using System.Text.Json;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using WebPush;

namespace Avalon.Api.Services;

public interface INotificationService
{
    Task RegisterSubscriptionAsync(Account account, string userAgent, PushSubscriptionRequest request,
        CancellationToken cancellationToken = default);
    Task SendNotificationAsync(Account account, string message, CancellationToken cancellationToken = default);
}

public class NotificationService : INotificationService
{
    private readonly NotificationConfig _config;
    private readonly VapidDetails _vapidDetails;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ILogger<NotificationService> _logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    public NotificationService(ILoggerFactory loggerFactory, NotificationConfig config, IDeviceRepository deviceRepository)
    {
        _logger = loggerFactory.CreateLogger<NotificationService>();
        _config = config;
        _deviceRepository = deviceRepository;
        _vapidDetails = new VapidDetails(_config.Subject, _config.PublicKey, _config.PrivateKey);
    }
    
    public async Task RegisterSubscriptionAsync(Account account, string userAgent, PushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {

        var devices = await _deviceRepository.FindByAsync(d => d.AccountId == account.Id);
        var device = devices.FirstOrDefault(x => x.Name == userAgent);
        
        if (device == null)
        {
            device = new Device
            {
                AccountId = account.Id,
                Name = userAgent,
                Metadata = JsonSerializer.Serialize(request),
                Trusted = false,
                TrustEnd = DateTime.UtcNow,
                LastUsage = DateTime.UtcNow,
            };
            
            await _deviceRepository.CreateAsync(device);
        }
        else
        {
            device.Metadata = JsonSerializer.Serialize(request);
            device.LastUsage = DateTime.UtcNow;
            
            await _deviceRepository.UpdateAsync(device);
        }
        
        var webPushClient = new WebPushClient();

        var payload = new
        {
            notification = new
            {
                title = "Welcome to Avalon",
                body = "You will now receive notifications from Avalon",
                icon = "https://avatars.githubusercontent.com/u/10047099?s=200&v=4",
                vibrate = new[] {100, 50, 100},
                data = new
                {
                    dateOfArrival = DateTime.Now,
                    primaryKey = 1
                },
                actions = new[]
                {
                    new
                    {
                        action = "explore",
                        title = "Go to the site"
                    }
                }
            }
        };
        
        var subscription = new PushSubscription(request.Endpoint, request.Keys.P256DH, request.Keys.Auth);
        
        var notification = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        await webPushClient.SendNotificationAsync(subscription, notification, _vapidDetails, cancellationToken);
    }

    public async Task SendNotificationAsync(Account account, string message, CancellationToken cancellationToken = default)
    {
        var devices = await _deviceRepository.FindByAsync(d => d.AccountId == account.Id);
        
        var webPushClient = new WebPushClient();
        webPushClient.SetVapidDetails(_vapidDetails);

        foreach (var device in devices)
        {
            try
            {
                await SendNotificationAsync(device, message, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to send notification to device {DeviceId}", device.Id);
            }
        }
    }

    private async Task SendNotificationAsync(Device device, string message, CancellationToken cancellationToken)
    {
        var deviceMetadata = JsonSerializer.Deserialize<PushSubscriptionRequest>(device.Metadata);
        if (deviceMetadata == null)
        {
            throw new InvalidOperationException("Device metadata is missing");
        }
        var subscription = new PushSubscription(deviceMetadata.Endpoint, deviceMetadata.Keys.P256DH, deviceMetadata.Keys.Auth);
        
        var payload = new
        {
            notification = new
            {
                title = "Avalon",
                body = message,
                icon = "https://avatars.githubusercontent.com/u/10047099?s=200&v=4",
                vibrate = new[] {100, 50, 100},
                data = new
                {
                    dateOfArrival = DateTime.Now,
                    primaryKey = 1
                },
                actions = new[]
                {
                    new
                    {
                        action = "explore",
                        title = "Go to the site"
                    }
                }
            }
        };
        
        var notification = JsonSerializer.Serialize(payload, _jsonSerializerOptions);
        
        var webPushClient = new WebPushClient();
        
        await webPushClient.SendNotificationAsync(subscription, notification, _vapidDetails, cancellationToken);
    }
}
