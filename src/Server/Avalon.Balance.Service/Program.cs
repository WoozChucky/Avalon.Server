using Avalon.Balance.Service;

WebApplication app = BalanceServiceHost.Build(WebApplication.CreateBuilder(args));

// Loaded and described now, not on the first request: a broken seed, config or catalog stops the pod at startup.
_ = app.Services.GetRequiredService<BalanceHost>().Catalog;

await app.RunAsync();
