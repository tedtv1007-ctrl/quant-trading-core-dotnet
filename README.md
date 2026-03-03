# Quant Trading Core (.NET 8)

High-frequency/Day-trading auxiliary system using C# .NET 8 Worker Service and gRPC.

## 🚀 Overview
This system is designed to convert subjective trading "intuition" into quantitative signals using an Event-Driven Architecture (EDA). It consumes high-frequency market data (Ticks/Bars) via gRPC and emits trading signals based on pre-defined strategies.

## 🧠 Strategies

### Strategy A: Pre-Market Gap (開盤試搓)
- **Goal:** Capture strength at 09:00 open.
- **Condition:** Simulated Price > Yesterday's Close * 1.01.
- **Filter:** Fakeout detection (monitors 08:59:55 for sharp pullbacks).

### Strategy B: Intraday Dip & Volume Surge (盤中低接反彈)
- **Goal:** Counter-trend buy on volume exhaustion.
- **Condition:** Price < VWAP - 2% AND Current Volume > 5-Bar Avg * 3.0.
- **Confirmation:** Trigger on the first upward tick after conditions are met.

## 🛠 Architecture
- **QuantTrading.Core**: Domain models, interfaces, and the core Strategy Engine.
- **QuantTrading.Worker**: .NET 8 Background Service handling DI and lifecycle.
- **QuantTrading.Grpc**: Proto definitions and gRPC service implementations for Market Data and Signals.

## 🛡 Risk Management
- **Single Stop Loss:** Fixed at 1,100 TWD per trade.
- **Position Sizing:** `1100 / (EntryPrice - StopLossPrice)`.
- **Daily Limit:** Max 5 "Grade A" signals per day.

## 📝 Development Status
- [x] Phase 1: Strategy Logic (A & B)
- [x] Phase 2: Risk Management Module
- [x] Phase 3: gRPC Proto Definitions
- [x] Phase 4: Worker Service & Consumer Implementation
- [ ] Phase 5: Integration Testing & Live Feed Connectivity
