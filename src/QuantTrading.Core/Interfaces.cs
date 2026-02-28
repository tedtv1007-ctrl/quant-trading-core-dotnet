using System;
using System.Threading.Tasks;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Interfaces;

public interface IMarketDataFeed
{
    event Action<TickData> OnTickReceived;
    event Action<BarData> OnBarClosed;
    void Subscribe(string ticker, MarketDataType dataType);
    void Unsubscribe(string ticker);
}

public interface IStrategyEngine
{
    Task ProcessTickAsync(TickData tick);
    Task ProcessBarAsync(BarData bar);
    event Action<SignalContext> OnSignalGenerated;
}

public interface IRiskManager
{
    SignalContext? CalculateAndValidate(StrategyType type, TickData tick, decimal stopLossPrice, double volumeRatio);
}
