# Decision Log - Quant Trading Core

This log records architectural decisions, logic pivots, and implementation details to maintain continuity across development sessions. (Adopted from Claude-Mem 3-layer pattern).

## [2026-03-01] Phase 3: gRPC Service Definition
- **Decision:** Define gRPC services for \`MarketDataService\` and \`SignalService\`.
- **Reasoning:** Ted's requirement for EDA and .NET 8 Worker Service communication.
- **Impact:** gRPC allows low-latency, strongly-typed streaming of ticks and bars, as well as real-time signal notification to trading clients.
- **Implemented:** \`market_data.proto\` and \`trading_signal.proto\` created in \`src/QuantTrading.Grpc/Protos\`.

## [2026-02-28] Initial Project Kick-off
- **Decision:** Use C# .NET 8 Worker Service + gRPC.
- **Decision:** Fixed Risk Limit: 1,100 TWD per trade, max 5 trades/day.
- **Decision:** Strategy A (08:59:55 Gap) & Strategy B (Intraday Dip + Vol Surge) defined.
