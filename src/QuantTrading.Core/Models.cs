using System;

namespace QuantTrading.Core.Models;

public enum StrategyType { OpenGap, IntradayDip }
public enum MarketDataType { Simulate, Realtime }

public record TickData(string Ticker, decimal Price, DateTime Timestamp);
public record BarData(string Ticker, decimal Open, decimal High, decimal Low, decimal Close, long Volume, DateTime Timestamp);

public record SignalContext(
    StrategyType Strategy,
    string Ticker,
    decimal EntryPrice,
    decimal StopLossPrice,
    double VolumeRatio,
    int PositionSize,
    DateTime Timestamp
);
