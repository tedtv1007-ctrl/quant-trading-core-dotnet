using QuantTrading.Core;
using QuantTrading.Core.Interfaces;
using QuantTrading.Worker;
using QuantTrading.Worker.Services;
using QuantTrading.Grpc;
using Grpc.Net.Client;

var builder = Host.CreateApplicationBuilder(args);

// 1. Core Services
builder.Services.AddSingleton<IRiskManager, RiskManager>();
builder.Services.AddSingleton<IStrategyEngine>(sp => 
    new StrategyEngine(sp.GetRequiredService<IRiskManager>(), refPrice: 100.0m));

// 2. gRPC Client Setup
builder.Services.AddSingleton(sp => 
{
    var channel = GrpcChannel.ForAddress("http://localhost:5000"); // Example server
    return new MarketDataService.MarketDataServiceClient(channel);
});

// 3. Worker Services
builder.Services.AddSingleton<MarketDataConsumer>();
builder.Services.AddHostedService<TradingWorker>();

var host = builder.Build();
host.Run();
