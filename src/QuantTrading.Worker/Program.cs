using QuantTrading.Core;
using QuantTrading.Core.Interfaces;
using QuantTrading.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Add Services
builder.Services.AddSingleton<IRiskManager, RiskManager>();
builder.Services.AddSingleton<IStrategyEngine>(sp => 
    new StrategyEngine(sp.GetRequiredService<IRiskManager>(), refPrice: 100.0m)); // Example refPrice

builder.Services.AddHostedService<TradingWorker>();

var host = builder.Build();
host.Run();
