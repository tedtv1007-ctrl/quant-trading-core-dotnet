using Grpc.Core;
using QuantTrading.Core.Interfaces;
using QuantTrading.Grpc;
using QuantTrading.Worker.Mappers;

namespace QuantTrading.Worker.Services;

public class MarketDataConsumer
{
    private readonly ILogger<MarketDataConsumer> _logger;
    private readonly IStrategyEngine _strategyEngine;
    private readonly MarketDataService.MarketDataServiceClient _client;

    public MarketDataConsumer(
        ILogger<MarketDataConsumer> logger, 
        IStrategyEngine strategyEngine,
        MarketDataService.MarketDataServiceClient client)
    {
        _logger = logger;
        _strategyEngine = strategyEngine;
        _client = client;
    }

    public async Task StartConsumingAsync(string ticker, SubscribeRequest.Types.DataType type, CancellationToken ct)
    {
        var request = new SubscribeRequest { Ticker = ticker, Type = type };
        using var call = _client.Subscribe(request, cancellationToken: ct);

        _logger.LogInformation("Started subscribing to {ticker} ({type})", ticker, type);

        await foreach (var update in call.ResponseStream.ReadAllAsync(ct))
        {
            switch (update.UpdateCase)
            {
                case MarketDataUpdate.UpdateOneofCase.Tick:
                    await _strategyEngine.ProcessTickAsync(update.Tick.ToInternal());
                    break;
                case MarketDataUpdate.UpdateOneofCase.Bar:
                    await _strategyEngine.ProcessBarAsync(update.Bar.ToInternal());
                    break;
            }
        }
    }
}
