# Decision Log - Quant Trading Core

This log records architectural decisions, logic pivots, and implementation details to maintain continuity across development sessions. (Adopted from Claude-Mem 3-layer pattern).

## [2026-03-02] Phase 4: Full Documentation & README Update
- **Decision:** Write comprehensive `README.md` to document strategy A/B and system architecture.
- **Reasoning:** Maintain clarity as the project scales.
- **Impact:** Clear onboarding for any developer (or AI) joining the project.

## [2026-03-02] Phase 4: Signal Publisher Implementation
- **Decision:** Implement `SignalPublisher.cs` to bridge `StrategyEngine` signals with gRPC service clients.
- **Reasoning:** Completing the "Event-Driven" feedback loop.
- **Impact:** Signals can now be emitted to trading execution bots in real-time.

## [2026-03-02] Phase 4: Full Strategy B Logic Implementation
- **Decision:** Fully implement Strategy B (Intraday Dip + Volume Surge) and VWAP logic in `StrategyEngine.cs`.
- **Reasoning:** Proactive decision by Milk after Ted's "開工吧" instruction.
- **Impact:** The strategy engine is now logically complete for both Strategy A and B.

## [2026-03-01] Phase 3: gRPC Service Definition
- **Decision:** Define gRPC services for `MarketDataService` and `SignalService`.
- **Reasoning:** Ted's requirement for EDA and .NET 8 Worker Service communication.
- **Impact:** gRPC allows low-latency, strongly-typed streaming of ticks and bars.

## [2026-02-28] Initial Project Kick-off
- **Decision:** Use C# .NET 8 Worker Service + gRPC.
- **Decision:** Fixed Risk Limit: 1,100 TWD per trade, max 5 trades/day.
- **Decision:** Strategy A (08:59:55 Gap) & Strategy B (Intraday Dip + Vol Surge) defined.
