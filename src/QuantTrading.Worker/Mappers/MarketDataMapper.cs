using QuantTrading.Core.Models;
using QuantTrading.Grpc; // Generated from .proto

namespace QuantTrading.Worker.Mappers;

public static class MarketDataMapper
{
    public static TickData ToInternal(this Tick grpcTick) => new(
        grpcTick.Ticker,
        (decimal)grpcTick.Price,
        grpcTick.Timestamp.ToDateTime()
    );

    public static BarData ToInternal(this Bar grpcBar) => new(
        grpcBar.Ticker,
        (decimal)grpcBar.Open,
        (decimal)grpcBar.High,
        (decimal)grpcBar.Low,
        (decimal)grpcBar.Close,
        grpcBar.Volume,
        grpcBar.Timestamp.ToDateTime()
    );
}
