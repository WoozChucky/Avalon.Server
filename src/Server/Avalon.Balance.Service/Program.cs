using Avalon.Balance.Service;

WebApplication app = BalanceServiceHost.Build(WebApplication.CreateBuilder(args));

// Loaded now, not on the first request: a broken seed or config stops the pod at startup.
app.Services.GetRequiredService<BalanceHost>();

await app.RunAsync();
