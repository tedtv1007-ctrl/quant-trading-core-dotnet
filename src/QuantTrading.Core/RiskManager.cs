using System;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Core;

public class RiskManager : IRiskManager
{
    private int _dailyTradeCount = 0;
    private decimal _totalDailyLoss = 0;
    private const decimal MaxDailyLoss = 5000; // Example limit
    private const int MaxDailyTrades = 5;
    private const decimal RiskPerTrade = 1100;

    public SignalContext? CalculateAndValidate(StrategyType type, TickData tick, decimal stopLossPrice, double volumeRatio)
    {
        if (_dailyTradeCount >= MaxDailyTrades || _totalDailyLoss >= MaxDailyLoss)
            return null;

        decimal riskPerShare = Math.Abs(tick.Price - stopLossPrice);
        if (riskPerShare <= 0) return null;

        int positionSize = (int)(RiskPerTrade / riskPerShare);

        _dailyTradeCount++;
        return new SignalContext(
            Strategy: type,
            Ticker: tick.Ticker,
            EntryPrice: tick.Price,
            StopLossPrice: stopLossPrice,
            VolumeRatio: volumeRatio,
            PositionSize: positionSize,
            Timestamp: DateTime.UtcNow
        );
    }
}
