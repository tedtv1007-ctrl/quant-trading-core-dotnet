# Decision Log - Quant Trading Core

This log records architectural decisions, logic pivots, and implementation details to maintain continuity across development sessions. (Adopted from Claude-Mem 3-layer pattern).

## [2026-03-01] Project Pivot: Adaptive Memory Implementation
- **Decision:** Adopt "Lifecycle Memory" logic into Milk's operating procedure.
- **Reasoning:** Ted's request to optimize actions based on `claude-mem`.
- **Impact:** Milk will now proactively update this log and daily memory files after major implementation milestones.

## [2026-02-28] Initial Project Kick-off
- **Decision:** Use C# .NET 8 Worker Service + gRPC.
- **Decision:** Fixed Risk Limit: 1,100 TWD per trade, max 5 trades/day.
- **Decision:** Strategy A (08:59:55 Gap) & Strategy B (Intraday Dip + Vol Surge) defined.
