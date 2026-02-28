using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Core;

public class StrategyEngine : IStrategyEngine
{
    private readonly IRiskManager _riskManager;
    private readonly List<BarData> _recentBars = new();
    private decimal _vwap;
    private decimal _refPrice;
    private decimal _lastTickPrice;
    private readonly double _thresholdPercent = 0.02; // Example 2%

    public event Action<SignalContext>? OnSignalGenerated;

    public StrategyEngine(IRiskManager riskManager, decimal refPrice)
    {
        _riskManager = riskManager;
        _refPrice = refPrice;
    }

    public async Task ProcessTickAsync(TickData tick)
    {
        await Task.Run(() =>
        {
            var time = tick.Timestamp.TimeOfDay;

            // Strategy A: Pre-Market Gap
            if (time >= new TimeSpan(8, 30, 0) && time <= new TimeSpan(8, 59, 55))
            {
                bool isStrongGap = tick.Price > _refPrice * 1.01m;
                if (time == new TimeSpan(8, 59, 55) && isStrongGap)
                {
                    GenerateSignal(StrategyType.OpenGap, tick, tick.Price * 0.99m, 1.0);
                }
            }

            // Strategy B logic part: Tick reversal check
            // ... (Full implementation in next phase)
            _lastTickPrice = tick.Price;
        });
    }

    public async Task ProcessBarAsync(BarData bar)
    {
        await Task.Run(() =>
        {
            _recentBars.Add(bar);
            if (_recentBars.Count > 100) _recentBars.RemoveAt(0);
            
            // Update VWAP etc.
        });
    }

    private void GenerateSignal(StrategyType type, TickData tick, decimal stopLoss, double volumeRatio)
    {
        var signal = _riskManager.CalculateAndValidate(type, tick, stopLoss, volumeRatio);
        if (signal != null) OnSignalGenerated?.Invoke(signal);
    }
}
