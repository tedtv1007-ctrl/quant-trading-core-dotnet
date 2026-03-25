using System;
using System.Collections.Concurrent;
using QuantTrading.Core.Models;

namespace QuantTrading.Core;

/// <summary>
/// 流動性風險評估器 — 獨立於原有的風控模組。
/// 當股價距離跌停板 <= 2% 時，若當前部位為多單，強制送出 MarketSell 訊號並暫停該標的當日的一切新進場訊號。
/// </summary>
public class LiquidityRiskEvaluator
{
    private readonly ConcurrentDictionary<string, bool> _suspendedTickers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _emergencySellTriggered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 檢查是否因流動性風險被暫停進場
    /// </summary>
    public bool IsSuspended(string ticker)
    {
        return _suspendedTickers.GetValueOrDefault(ticker, false);
    }

    /// <summary>
    /// 評估是否觸發緊急停損
    /// </summary>
    public bool TryEvaluateEmergencySell(TickData tick, decimal refPrice, bool hasLongPosition, out SignalContext? emergencySignal)
    {
        emergencySignal = null;

        if (refPrice <= 0) return false;

        // 計算跌停價 (台股現行約為前日收盤價 - 10%，簡化計算)
        decimal limitDownPrice = Math.Floor(refPrice * 0.90m); 
        
        // 距離跌停 <= 2%
        decimal thresholdPrice = limitDownPrice * 1.02m;

        if (tick.Price <= thresholdPrice)
        {
            // 標記暫停進場
            _suspendedTickers[tick.Ticker] = true;

            if (hasLongPosition && !_emergencySellTriggered.GetValueOrDefault(tick.Ticker, false))
            {
                _emergencySellTriggered[tick.Ticker] = true;
                
                emergencySignal = new SignalContext(
                    Strategy: StrategyType.IntradayDip, // TODO: 可新增 StrategyType.LiquidityRisk
                    OrderType: OrderType.MarketSell,
                    Ticker: tick.Ticker,
                    EntryPrice: tick.Price,
                    StopLossPrice: 0,
                    VolumeRatio: 0,
                    PositionSize: 0, // 應交由實際下單模組決定
                    Timestamp: tick.Timestamp
                );
                return true;
            }
        }

        return false;
    }
}
