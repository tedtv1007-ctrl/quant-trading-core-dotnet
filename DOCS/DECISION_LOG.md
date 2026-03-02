# Decision Log - Quant Trading Core

This log records architectural decisions, logic pivots, and implementation details to maintain continuity across development sessions. (Adopted from Claude-Mem 3-layer pattern).

## [2026-03-02] Phase 4: Market Data Consumer Implementation
- **Decision:** Implement \`MarketDataConsumer\` and \`MarketDataMapper\` for gRPC stream ingestion.
- **Reasoning:** Proactive decision to connect gRPC market data streams directly to the strategy engine.
- **Impact:** Enabled real-time processing of Ticks and Bars. Transitioned from manual simulation to a reactive gRPC-driven model.
- **Components:** \`MarketDataConsumer.cs\`, \`MarketDataMapper.cs\`, and updated \`TradingWorker.cs\`.

## [2026-03-02] Phase 4: Worker Service Architecture Implementation
- **Decision:** Implement the `QuantTrading.Worker` project as the central execution host.
- **Reasoning:** Proactive decision to bridge strategy logic with gRPC event streams.
- **Impact:** Sets up Dependency Injection (DI) and lifecycle management for the `StrategyEngine`.

## [2026-03-01] Phase 3: gRPC Service Definition
- **Decision:** Define gRPC services for `MarketDataService` and `SignalService`.
- **Reasoning:** Ted's requirement for EDA and .NET 8 Worker Service communication.
- **Impact:** gRPC allows low-latency, strongly-typed streaming of ticks and bars.

## [2026-02-28] Initial Project Kick-off
- **Decision:** Use C# .NET 8 Worker Service + gRPC.
- **Decision:** Fixed Risk Limit: 1,100 TWD per trade, max 5 trades/day.
- **Decision:** Strategy A (08:59:55 Gap) & Strategy B (Intraday Dip + Vol Surge) defined.
