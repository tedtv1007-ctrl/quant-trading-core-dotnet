using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;
using QuantTrading.Grpc;
using QuantTrading.Worker.Services;

namespace QuantTrading.Worker;

public class TradingWorker : BackgroundService
{
    private readonly ILogger<TradingWorker> _logger;
    private readonly IStrategyEngine _strategyEngine;
    private readonly MarketDataConsumer _marketDataConsumer;

    public TradingWorker(
        ILogger<TradingWorker> logger, 
        IStrategyEngine strategyEngine,
        MarketDataConsumer marketDataConsumer)
    {
        _logger = logger;
        _strategyEngine = strategyEngine;
        _marketDataConsumer = marketDataConsumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Trading Worker starting at: {time}", DateTimeOffset.Now);

        // 1. Subscribe to strategy signals
        _strategyEngine.OnSignalGenerated += (signal) =>
        {
            _logger.LogWarning("!!! [Strategy] SIGNAL: {strategy} for {ticker} at {price} (Size: {size})",
                signal.Strategy, signal.Ticker, signal.EntryPrice, signal.PositionSize);
            
            // TODO: Emit via SignalService.gRPC to execution clients
        };

        // 2. Start consuming market data for target tickers
        var tickers = new[] { "AAPL", "MSFT", "TSLA" }; // Example target list
        
        try
        {
            var tasks = tickers.Select(ticker => 
                _marketDataConsumer.StartConsumingAsync(ticker, SubscribeRequest.Types.DataType.Realtime, stoppingToken));

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Trading Worker stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trading Worker encountered an error.");
        }
    }
}
