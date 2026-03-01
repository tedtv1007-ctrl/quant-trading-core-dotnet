# Strategy Specification - Quantitative Trading System

## Role & Context
- **Role:** Senior Quantitative System Architect & C# Developer
- **Objective:** Develop a high-frequency/day-trading auxiliary system.
- **Architecture:** Event-Driven Architecture (EDA), C# .NET 8 Blazor Server, REST API.

## Phase 1: Strategy Logic Definition

### Strategy A: Pre-Market Gap (開盤試搓策略)
- **Target:** Capture strong 09:00 opening signals.
- **Input:** 08:30 ~ 09:00 Simulated Ticks.
- **Condition 1 (Gap Strength):** SimPrice > RefPrice * 1.01 (Gain > 1%).
- **Condition 2 (Fakeout Filter):** Monitor until 08:59:55 to ensure no sharp pullbacks (prevent spoofing).
- **Action:** 09:00:00 Market Buy.

### Strategy B: Intraday Dip & Volume Surge (盤中低接反彈)
- **Target:** Counter-trend operation at high volume points after sharp falls.
- **Input:** Real-time Ticks & 1-Min Bars.
- **Condition 1 (Dip Detection):** Price < VWAP - Threshold_Percent.
- **Condition 2 (Volume Spike):** Current 1-Min Volume > (Avg 5-Bar Volume * 3.0).
- **Action:** Confirm trend reversal (next tick up) -> Limit Buy.

## Phase 2: Risk Calculation
- **Constraint:** Max 5 orders per day (all strategy types combined).
- **Single Stop Loss:** 1,100 TWD.
- **Position Size Calculation:** `PositionSize = 1100 / (EntryPrice - StopLossPrice)`.
- **Global Stop:** Reject if daily loss > MaxDailyLoss or trades >= 5.

## Phase 3: Architecture & Interfaces
- **IMarketDataFeed:** Support Simulate and Realtime data streams.
- **StrategyEngine:** Logic implementation for Strategy A and B.
- **SignalContext:** Ticker, EntryPrice, StopLossPrice, VolumeRatio, StrategyType.
