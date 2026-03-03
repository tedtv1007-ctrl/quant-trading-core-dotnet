using Grpc.Core;
using QuantTrading.Core.Models;
using QuantTrading.Grpc;

namespace QuantTrading.Worker.Services;

/// <summary>
/// 訊號發送服務 (SignalPublisher) — 將 StrategyEngine 產生的訊號透過 gRPC 發送給交易端。
/// </summary>
public class SignalPublisher
{
    private readonly ILogger<SignalPublisher> _logger;
    private readonly SignalService.SignalServiceClient _client;

    public SignalPublisher(ILogger<SignalPublisher> logger, SignalService.SignalServiceClient client)
    {
        _logger = logger;
        _client = client;
    }

    public async Task PublishAsync(SignalContext signal, CancellationToken ct)
    {
        var grpcSignal = new TradeSignal
        {
            Ticker = signal.Ticker,
            Strategy = signal.Strategy == StrategyType.OpenGap 
                ? TradeSignal.Types.StrategyType.OpenGap 
                : TradeSignal.Types.StrategyType.IntradayDip,
            EntryPrice = (double)signal.EntryPrice,
            StopLoss = (double)signal.StopLossPrice,
            PositionSize = signal.PositionSize,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(signal.Timestamp.ToUniversalTime())
        };

        try
        {
            // 在此處實作：將訊號寫入一個長連線 Stream 或呼叫單次 RPC。
            // 目前設計為發送即忘 (Fire and forget) 或存入發送隊列。
            _logger.LogInformation(">>> [gRPC] Sending SIGNAL to execution client: {ticker} @ {price}", 
                signal.Ticker, signal.EntryPrice);
            
            // 範例：若 SignalService 定義了單次發送 RPC
            // await _client.SendSignalAsync(grpcSignal, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish signal for {ticker} over gRPC.", signal.Ticker);
        }
    }
}
