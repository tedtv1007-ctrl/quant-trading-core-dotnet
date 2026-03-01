# QuantTrading Core — 系統架構說明文件

> **版本:** v2.0 | **最後更新:** 2026-03-01 | **框架:** .NET 8 + Blazor Server  
> **測試覆蓋:** 110 tests (89 Unit + 21 E2E) — 全部通過

---

## 目錄

1. [系統概觀](#1-系統概觀)
2. [技術棧](#2-技術棧)
3. [專案結構 & 分層架構](#3-專案結構--分層架構)
4. [核心領域模型（Core Layer）](#4-核心領域模型core-layer)
5. [策略引擎詳解](#5-策略引擎詳解)
6. [風控管理器詳解](#6-風控管理器詳解)
7. [基礎設施層（Infrastructure Layer）](#7-基礎設施層infrastructure-layer)
8. [Web 展示層（Presentation Layer）](#8-web-展示層presentation-layer)
9. [資料流架構](#9-資料流架構)
10. [DI 容器配置](#10-di-容器配置)
11. [持久化機制](#11-持久化機制)
12. [執行緒安全設計](#12-執行緒安全設計)
13. [測試架構](#13-測試架構)
14. [擴展指南](#14-擴展指南)
15. [附錄：介面清單](#15-附錄介面清單)

---

## 1. 系統概觀

QuantTrading Core 是一套**台灣股市日內交易輔助系統**，採用事件驅動架構（Event-Driven Architecture），
支援兩種量化策略的模擬回測與即時訊號產出。

### 系統定位

```
行情資料 (Tick/Bar)
        │
        ▼
┌─────────────────┐    訊號     ┌─────────────┐    允許/拒絕    ┌───────────────┐
│  Strategy Engine │ ─────────▶ │ Risk Manager │ ────────────▶ │ Signal Output │
│  (策略 A + B)    │            │  (風控閘門)   │               │  (事件通知)    │
└─────────────────┘            └─────────────┘               └───────────────┘
                                                                     │
                                                                     ▼
                                                           ┌─────────────────┐
                                                           │ Blazor Dashboard │
                                                           │   + REST API     │
                                                           └─────────────────┘
```

### 核心功能

| 功能 | 說明 |
|------|------|
| **策略 A: 盤前試搓** | 08:30–09:00 監測試搓 Tick，偵測跳空強勢 → 09:00 Market Buy |
| **策略 B: 盤中低接** | 09:01–13:25 監測 Bar+Tick，偵測量能放大低接反彈 → Limit Buy |
| **風控管理** | 每日最多 5 筆、單筆停損 1,100 TWD、全域累計虧損上限 |
| **多股同步模擬** | 同時模擬多檔股票的完整交易日 |
| **即時 Dashboard** | Blazor Server 暗色交易終端風格 UI |
| **REST API** | 11 個 Minimal API Endpoints |
| **交易日誌** | 手動記錄每日實際成交，JSON 持久化 |
| **組態管理** | 所有參數可即時編輯，JSON 持久化 |

---

## 2. 技術棧

| 層級 | 技術 | 說明 |
|------|------|------|
| **語言** | C# 12 / .NET 8 | LTS 版本，高效能 |
| **Web 框架** | Blazor Server (Interactive) | 即時雙向 SignalR 通訊 |
| **API** | ASP.NET Core Minimal API | 輕量 REST Endpoints |
| **即時行情** | Fugle WebSocket v1.0 | 台灣股市即時串流（選配） |
| **持久化** | JSON File Store | 輕量無 DB 依賴 |
| **網路韌性** | Polly 8.x | Exponential Backoff 重連 |
| **測試** | xUnit + FluentAssertions 8.8 | BDD 風格斷言 |
| **E2E 測試** | WebApplicationFactory | 真實 HTTP 管線測試 |
| **UI** | Bootstrap 5 + Bootstrap Icons | 暗色交易終端主題 |

---

## 3. 專案結構 & 分層架構

### 3.1 Solution 結構

```
quant-trading-core-dotnet/
│
├── quant-trading-core-dotnet.sln        # Visual Studio 2022 Solution
├── build.bat                            # 建置 + 全部測試
├── run-web.bat                          # 啟動 Web Dashboard
├── run-tests.bat                        # 只跑測試
│
├── src/
│   ├── QuantTrading.Core/               # 【核心層】策略、風控、Models
│   │   ├── Models.cs                    #   所有領域模型 & Enums
│   │   ├── Interfaces.cs               #   所有抽象介面
│   │   ├── StrategyEngine.cs           #   策略引擎實作
│   │   ├── RiskManager.cs              #   風控管理器實作
│   │   └── TradingEngineFactory.cs     #   引擎工廠（防狀態殘留）
│   │
│   ├── QuantTrading.Infrastructure/     # 【基礎設施層】外部整合
│   │   ├── Configuration/              #   組態/日誌持久化
│   │   │   ├── JsonConfigurationStore.cs
│   │   │   ├── JsonTradeJournalStore.cs
│   │   │   └── FugleOptions.cs
│   │   ├── Fugle/                      #   Fugle WebSocket 整合
│   │   │   ├── FugleMarketDataFeed.cs  #     行情串流引擎 (817 行)
│   │   │   ├── FugleMarketDataHostedService.cs
│   │   │   └── FugleMessages.cs        #     WebSocket 協定 DTOs
│   │   └── FugleServiceExtensions.cs   #   DI 擴充方法
│   │
│   └── QuantTrading.Web/               # 【展示層】Blazor + API
│       ├── Program.cs                  #   DI 容器 & Pipeline
│       ├── Components/
│       │   ├── Layout/                 #   NavMenu、MainLayout
│       │   └── Pages/                  #   5 個功能頁面
│       │       ├── Home.razor          #     Dashboard 總覽
│       │       ├── Simulation.razor    #     模擬操控台
│       │       ├── Signals.razor       #     訊號/行情日誌
│       │       ├── Configuration.razor #     參數設定
│       │       └── TradeJournal.razor  #     交易日誌
│       ├── Services/                   #   背景服務 & API
│       │   ├── TradingStateService.cs  #     狀態中心 (Singleton)
│       │   ├── MarketDataSimulator.cs  #     合成行情產生器
│       │   ├── SimulationBackgroundService.cs  # 多股模擬排程
│       │   └── TradingApiEndpoints.cs  #     Minimal API Endpoints
│       ├── Data/                       #   持久化 JSON 檔案
│       └── wwwroot/                    #   CSS (暗色主題)
│
├── tests/
│   ├── QuantTrading.Core.Tests/        # 89 個單元測試
│   └── QuantTrading.E2E.Tests/         # 21 個 E2E 測試
│
└── DOCS/
    ├── ARCHITECTURE.md                 # ← 本文件
    ├── USER_MANUAL.md                  # 使用手冊
    ├── STRATEGY_SPEC.md                # 策略規格書
    └── STARTUP.md                      # 啟動手冊
```

### 3.2 分層依賴圖

```
┌──────────────────────────────┐
│     QuantTrading.Web         │  ← 展示層（Blazor + API）
│  (Blazor Server, REST API)   │
└──────────┬───────────────────┘
           │ references
           ▼
┌──────────────────────────────┐
│ QuantTrading.Infrastructure  │  ← 基礎設施層（Fugle、JSON Store）
│  (WebSocket, File I/O)       │
└──────────┬───────────────────┘
           │ references
           ▼
┌──────────────────────────────┐
│     QuantTrading.Core        │  ← 核心層（零外部依賴）
│  (Models, Interfaces, Logic) │
└──────────────────────────────┘
```

**Core 層零依賴原則：** `QuantTrading.Core.csproj` 不引用任何 NuGet 套件，
確保策略邏輯可在任何環境測試與重用。

---

## 4. 核心領域模型（Core Layer）

### 4.1 Enums

```csharp
enum StrategyType    { OpenGap, IntradayDip }         // 策略 A / B
enum MarketDataType  { Simulate, Realtime }            // 模擬 / 即時
enum OrderType       { MarketBuy, LimitBuy }           // 市價 / 限價
enum SignalResult    { Accept, RejectRisk,              // 風控結果
                       RejectMaxTrades, RejectDailyLoss }
enum TradeDirection  { Buy, Sell }                     // 交易方向
```

### 4.2 行情資料

| 類型 | 用途 | 關鍵欄位 |
|------|------|----------|
| `TickData` | 逐筆成交 / 試搓報價 | Ticker, Price, Volume, Timestamp |
| `BarData` | 1 分鐘 K 棒 | Ticker, OHLCV, Timestamp |

### 4.3 訊號輸出

| 類型 | 用途 | 關鍵欄位 |
|------|------|----------|
| `SignalContext` | 策略產出的交易訊號 | Strategy, OrderType, Ticker, EntryPrice, StopLossPrice, VolumeRatio, PositionSize, Timestamp |
| `RejectedSignal` | 被風控拒絕的訊號 | Reason, Strategy, Ticker, Timestamp |

### 4.4 組態模型

```
PreMarketGapConfig          IntradayDipConfig           RiskConfig
├── MonitorStart (08:30)    ├── ActiveStart (09:01)     ├── RiskPerTrade (1,100)
├── MonitorEnd (08:59:55)   ├── ActiveEnd (13:25)       ├── MaxDailyLoss (5,000)
├── ExecutionTime (09:00)   ├── DipThresholdPercent(2%) └── MaxDailyTrades (5)
├── GapStrengthPercent(1%)  ├── VolumeSpikeMultiplier(3)
├── FakeoutPullback(0.5%)   ├── VolumeLookbackBars (5)
└── StopLossOffset(1%)      └── StopLossOffsetPercent(1%)
```

### 4.5 交易日誌

```csharp
class TradeRecord
├── Id                  // 唯一識別碼 (GUID 前 8 碼)
├── TradeDate           // 交易日期
├── Ticker              // 股票代號
├── Direction           // Buy / Sell
├── Price               // 成交價格
├── Quantity            // 成交股數
├── Strategy            // 手動標記策略
├── Note                // 備註
├── CreatedAt           // 建立時間
└── Amount              // 計算屬性 = Price × Quantity
```

### 4.6 TradingConfiguration（執行緒安全組態容器）

```csharp
class TradingConfiguration
{
    private readonly object _lock = new();
    GapConfig  { get/set with lock }
    DipConfig  { get/set with lock }
    RiskConfig { get/set with lock }
    event OnConfigChanged;        // 組態變更通知
    void NotifyChanged();
}
```

---

## 5. 策略引擎詳解

### 5.1 StrategyEngine 類別

`StrategyEngine` 實作 `IStrategyEngine`，是整個系統的交易決策核心。

```
StrategyEngine
├── Dependencies
│   ├── IRiskManager          (風控閘門)
│   ├── PreMarketGapConfig    (策略 A 參數)
│   └── IntradayDipConfig     (策略 B 參數)
│
├── ProcessTickAsync(TickData)
│   ├── 08:30–09:00 → EvaluatePreMarketGap()     [策略 A]
│   └── 09:01–13:25 → EvaluateIntradayDipTick()  [策略 B]
│
├── ProcessBarAsync(BarData)
│   ├── 維護 Bar 歷史 (滑動窗口 100)
│   ├── 更新 VWAP
│   └── EvaluateIntradayDipBar()  [量能偵測]
│
└── EmitSignal()
    └── RiskManager.EvaluateSignal() → Accept / Reject
```

### 5.2 Strategy A: 盤前試搓（Pre-Market Gap）

```
時間軸:
08:30          08:59:55        09:00
  ├──── 監測期 ────┤── 判定 ──┤
  │                │          │
  │  追蹤 SimHigh  │  驗證:   │
  │  偵測 Fakeout  │  Gap>1%  │
  │                │  無假突破 │
  │                │  → 發訊號│
```

**內部狀態（PreMarketState）：**
- `SimHighPrice` — 試搓期間最高價
- `LatestSimPrice` — 最新試搓價
- `FakeoutDetected` — 是否偵測到急跌假突破
- `SignalEmitted` — 是否已觸發（防重複）

**流程：**
1. 每筆 Tick 更新 SimHighPrice
2. 偵測是否有 > FakeoutPullbackPercent 的急跌
3. 08:59:55 時判定：
   - LatestSimPrice > RefPrice × (1 + GapStrengthPercent)
   - FakeoutDetected == false
4. 通過 → `EmitSignal(MarketBuy)`

### 5.3 Strategy B: 盤中低接反彈（Intraday Dip）

```
Bar 處理流程:                    Tick 處理流程:
                                
Bar → 更新 VWAP                 Tick → Price < VWAP−門檻?
    → 計算 Volume Ratio               ├─ Yes → 標記 DipConfirmed
    → Ratio ≥ 3.0?                    │         NextTick > DipTick?
       ├─ Yes → VolumeSpikeDetected   │         ├─ Yes → EmitSignal(LimitBuy)
       └─ No                          │         └─ No → 等待反彈
                                      └─ No
```

**內部狀態（IntradayState）：**
- `VwapNumerator` / `VwapDenominator` — VWAP 累計計算
- `RecentBarVolumes` — 最近 N 根 Bar 量能（滑動窗口）
- `VolumeSpikeDetected` — 量能放大旗標
- `LatestVolumeRatio` — 最新量比
- `DipConfirmed` — 低接確認旗標
- `DipPrice` — 低接價格

**VWAP 計算：**
$$VWAP = \frac{\sum_{i} (TypicalPrice_i \times Volume_i)}{\sum_{i} Volume_i}$$

其中 $TypicalPrice = \frac{High + Low + Close}{3}$

**低接門檻：**
$$Threshold = VWAP \times (1 - DipThresholdPercent)$$

### 5.4 EmitSignal 流程

```
策略判定通過
    │
    ▼
RiskManager.EvaluateSignal(strategy, orderType, tick, stopLoss, volRatio)
    │
    ├── Accept → OnSignalGenerated 事件
    │            (附帶完整 SignalContext)
    │
    └── Reject → OnSignalRejected 事件
                 (附帶 RejectedSignal + 拒絕原因)
```

---

## 6. 風控管理器詳解

### 6.1 RiskManager 類別

```
RiskManager (thread-safe with lock)
│
├── EvaluateSignal()
│   ├── Gate 1: DailyTradeCount < MaxDailyTrades     → RejectMaxTrades
│   ├── Gate 2: DailyRealizedLoss < MaxDailyLoss     → RejectDailyLoss
│   └── Pass:
│       ├── StopLossPrice = EntryPrice × (1 - StopLossOffset)
│       ├── PositionSize = RiskPerTrade / |Entry - StopLoss|
│       ├── DailyTradeCount++
│       └── Return (SignalContext, Accept)
│
├── RecordRealizedLoss(amount)    // 累計虧損
├── ResetDaily()                  // 每日重置
└── GetCurrentStatus()            // 查詢當前狀態
```

### 6.2 部位計算公式

$$PositionSize = \frac{RiskPerTrade}{|EntryPrice - StopLossPrice|}$$

**範例：**
- RiskPerTrade = 1,100 TWD
- EntryPrice = 600, StopLoss = 594 (1% offset)
- PositionSize = 1,100 / 6 ≈ **183 股**

### 6.3 風控閘門順序

```
                ┌─────────────────────┐
                │ 今日交易 ≥ 5 筆?    │
                │  Yes → RejectMaxTrades│
                └──────┬──────────────┘
                       │ No
                       ▼
                ┌─────────────────────┐
                │ 累計虧損 ≥ 上限?    │
                │  Yes → RejectDailyLoss│
                └──────┬──────────────┘
                       │ No
                       ▼
                ┌─────────────────────┐
                │ 計算部位 & Accept   │
                └─────────────────────┘
```

---

## 7. 基礎設施層（Infrastructure Layer）

### 7.1 Fugle WebSocket 整合

`FugleMarketDataFeed` 是最複雜的基礎設施元件（817 行），實作 `IMarketDataFeed`。

```
┌─────────────────────────────────────────────────────┐
│                FugleMarketDataFeed                    │
│                                                       │
│  ┌──────────┐    ┌───────────┐    ┌───────────────┐ │
│  │ WebSocket │───▶│ ReceiveLoop│───▶│ Channel<Tick>│─┼──▶ OnTickReceived
│  │  Client   │    │ (Parser)   │    │ Channel<Bar> │─┼──▶ OnBarClosed
│  └──────────┘    └───────────┘    └───────────────┘ │
│       ▲                                               │
│       │                                               │
│  ┌──────────┐    ┌───────────┐                       │
│  │ PingLoop │    │ Polly     │                       │
│  │ (30s)    │    │ Reconnect │                       │
│  └──────────┘    │ (Exp.Back)│                       │
│                  └───────────┘                       │
└─────────────────────────────────────────────────────┘
```

**關鍵設計：**
- `Channel<T>` Bounded Buffer（Producer-Consumer 模式）
- Polly Exponential Backoff 自動重連
- 30 秒 Ping 心跳保活
- Lock-protected 訂閱管理
- 支援頻道：trades, candles, books, aggregates, indices

### 7.2 WebSocket 協定（FugleMessages.cs）

```
Client → Server:
  {"event":"auth",      "data":{"apiToken":"..."}}
  {"event":"subscribe", "data":{"channel":"trades","symbol":"2330"}}
  {"event":"unsubscribe","data":{"channel":"trades","symbol":"2330"}}

Server → Client:
  {"event":"authenticated","data":{...}}
  {"event":"snapshot",    "data":{"channel":"trades","symbol":"2330",...}}
  {"event":"data",        "data":{"channel":"trades","symbol":"2330",...}}
  {"event":"subscribed",  "data":{...}}
```

### 7.3 JSON 持久化

| Store | 介面 | 檔案路徑 | 用途 |
|-------|------|----------|------|
| `JsonConfigurationStore` | `IConfigurationStore` | `Data/trading-config.json` | 交易參數組態 |
| `JsonTradeJournalStore` | `ITradeJournalStore` | `Data/trade-journal.json` | 交易日誌紀錄 |

兩者皆使用 `SemaphoreSlim(1,1)` 確保執行緒安全的非同步 I/O。

---

## 8. Web 展示層（Presentation Layer）

### 8.1 頁面架構

| 路由 | 頁面 | 功能 |
|------|------|------|
| `/` | Home.razor | Dashboard 總覽：狀態卡片、每股即時價、訊號/Tick 表格 |
| `/simulation` | Simulation.razor | 模擬操控台：監控清單、快速新增、啟動/停止/清除 |
| `/signals` | Signals.razor | 訊號日誌：篩選、Tab 切換（Signals/Rejected/Ticks/Bars）|
| `/trades` | TradeJournal.razor | 交易日誌：CRUD 表單、日期篩選、每日摘要、CSV 匯出 |
| `/configuration` | Configuration.razor | 參數設定：15 個可編輯欄位、存檔/重置 |

### 8.2 UI 設計系統

```css
/* 暗色交易終端主題 */
--qt-bg:       #0a0e17    /* 主背景 */
--qt-surface:  #111827    /* 卡片背景 */
--qt-border:   #1e293b    /* 邊框 */
--qt-text:     #e2e8f0    /* 主文字 */
--qt-accent:   #06b6d4    /* 強調色 (Cyan) */
--qt-green:    #22c55e    /* 漲 / Buy */
--qt-red:      #ef4444    /* 跌 / Sell */
--qt-yellow:   #eab308    /* 警告 */

/* 字型 */
font-family: 'Inter', sans-serif      /* UI 文字 */
font-family: 'JetBrains Mono'         /* 數字/代碼 */
```

### 8.3 TradingStateService（狀態中心）

```
TradingStateService (Singleton, thread-safe)
│
├── Watchlist Management
│   ├── AddToWatchlist(ticker, refPrice, status)
│   ├── RemoveFromWatchlist(ticker)
│   └── GetWatchlist() → List<WatchlistEntry>
│
├── Signal/Data Queues (bounded sliding windows)
│   ├── signals     → Queue(500)
│   ├── rejections  → Queue(500)
│   ├── ticks       → Queue(1000)
│   └── bars        → Queue(500)
│
├── Simulation State
│   ├── IsSimulationRunning
│   ├── SimulationStatus ("Idle"/"Running"/"Completed")
│   └── DailyTradeCount / DailyRealizedLoss
│
└── Event: OnStateChanged → UI 自動 refresh
```

### 8.4 REST API Endpoints

所有 API 位於 `/api/trading` 下：

```
GET    /api/trading/watchlist              # 取得監控清單
POST   /api/trading/watchlist              # 新增股票
DELETE /api/trading/watchlist/{ticker}      # 移除股票

POST   /api/trading/simulate              # 啟動模擬
POST   /api/trading/stop                  # 停止模擬

GET    /api/trading/signals?ticker=       # 策略訊號
GET    /api/trading/rejections?ticker=    # 風控拒絕
GET    /api/trading/status                # 系統狀態
GET    /api/trading/ticks?ticker=         # 近期 Tick
GET    /api/trading/bars?ticker=          # 近期 Bar

GET    /api/trading/journal/export?from=&to=  # 匯出交易日誌 CSV（可選日期區間）
```

### 8.5 模擬背景服務

```
SimulationBackgroundService
│
├── StartSimulation(date, delayMs)
│   └── RunMultiStockSimulationAsync()
│       │
│       ├── 1. ITradingEngineFactory.Create(configs)
│       │       → 建立全新 Engine + RiskManager (防狀態殘留)
│       │
│       ├── 2. Wire Events
│       │       Engine.OnSignalGenerated → StateService.AddSignal
│       │       Engine.OnSignalRejected  → StateService.AddRejection
│       │
│       ├── 3. MarketDataSimulator.GenerateFullDayScenario()
│       │       → 為每檔股票產生合成行情
│       │
│       ├── 4. Phase 1: Pre-Market Ticks (all stocks in parallel)
│       │       → Task.WhenAll(stock1Ticks, stock2Ticks, ...)
│       │
│       └── 5. Phase 2: Intraday Bars + Ticks (all stocks in parallel)
│               → Task.WhenAll(stock1Intraday, stock2Intraday, ...)
│
└── StopSimulation()
    └── CancellationTokenSource.Cancel()
```

---

## 9. 資料流架構

### 9.1 模擬模式完整資料流

```
User clicks "Start Simulation"
         │
         ▼
SimulationBackgroundService
         │
         ├─── TradingEngineFactory.Create()
         │         │
         │         ├── new RiskManager(riskConfig)
         │         └── new StrategyEngine(riskManager, gapConfig, dipConfig)
         │
         ├─── MarketDataSimulator.GenerateFullDayScenario()
         │         │
         │         ├── PreMarketTicks (08:30-09:00)
         │         └── IntradayBars + IntradayTicks (09:01-13:25)
         │
         ├─── Engine.ProcessTickAsync(tick)
         │         │
         │         ├── Strategy A 判定 → EmitSignal()
         │         │                         │
         │         │                    RiskManager.EvaluateSignal()
         │         │                         │
         │         │                    ┌────┴────┐
         │         │                 Accept     Reject
         │         │                    │          │
         │         │         OnSignalGenerated  OnSignalRejected
         │         │                    │          │
         │         │                    ▼          ▼
         │         │            StateService.AddSignal / AddRejection
         │         │                    │
         │         │            OnStateChanged event
         │         │                    │
         │         │            Blazor UI re-render
         │         │
         │         └── Strategy B 判定 (Tick 部分)
         │
         └─── Engine.ProcessBarAsync(bar)
                   │
                   ├── 更新 VWAP
                   └── Strategy B 判定 (Bar 部分 — 量能偵測)
```

### 9.2 即時模式資料流（Fugle）

```
Fugle WebSocket Server
         │
    WebSocket frames
         │
         ▼
FugleMarketDataFeed
         │
    ReceiveLoop (JSON parse)
         │
    ┌────┴────┐
Channel<Tick> Channel<Bar>
    │              │
DispatchLoop   DispatchLoop
    │              │
OnTickReceived OnBarClosed
    │              │
    └──────┬───────┘
           │
    StrategyEngine
           │
       (同模擬模式後續流程)
```

---

## 10. DI 容器配置

`Program.cs` 中的服務註冊：

```csharp
// ── 基礎設施 ──
services.AddSingleton<IConfigurationStore>(
    new JsonConfigurationStore("Data/trading-config.json"));
services.AddSingleton<ITradeJournalStore>(
    new JsonTradeJournalStore("Data/trade-journal.json"));

// ── 組態（啟動時載入 JSON） ──
services.AddSingleton<TradingConfiguration>(loaded from store);
services.AddSingleton(sp => sp.GetRequired<TradingConfiguration>().GapConfig);
services.AddSingleton(sp => sp.GetRequired<TradingConfiguration>().DipConfig);
services.AddSingleton(sp => sp.GetRequired<TradingConfiguration>().RiskConfig);

// ── 核心 ──
services.AddSingleton<ITradingEngineFactory, TradingEngineFactory>();

// ── Web 服務 ──
services.AddSingleton<TradingStateService>();
services.AddSingleton<MarketDataSimulator>();
services.AddSingleton<SimulationBackgroundService>();

// ── Fugle（選配，預設註解） ──
// services.AddFugleMarketDataFeed(configuration);
```

**生命週期策略：**
- 全部使用 `Singleton` — 因為 Blazor Server 的 Circuit 需要共享狀態
- `TradingEngineFactory` 每次 `Create()` 產生全新實例，避免跨次模擬汙染

---

## 11. 持久化機制

### 11.1 組態持久化

```
                    JsonConfigurationStore
                           │
                    ┌──────┴──────┐
                  Load           Save
                    │              │
        Data/trading-config.json ◄─┤
                    │              │
                    ▼              │
          TradingConfiguration ────┘
                    │
          GapConfig / DipConfig / RiskConfig
```

**DTO 轉換層：** 內部使用 `ConfigDto` 避免 `TimeSpan` 序列化問題，
以 `TimeSpanJsonConverter` 處理 `HH:mm:ss` 格式。

### 11.2 交易日誌持久化

```
          ITradeJournalStore
                │
        ┌───────┴───────┐
     GetAll          Add/Update/Delete
        │                │
   List<TradeRecord> ◄───┤
        │                │
  Data/trade-journal.json
```

**JSON 格式範例：**
```json
[
  {
    "id": "a1b2c3d4",
    "tradeDate": "2026-03-01T00:00:00",
    "ticker": "2330",
    "direction": "Buy",
    "price": 612,
    "quantity": 1000,
    "strategy": "Strategy A",
    "note": "盤前跳空買進",
    "createdAt": "2026-03-01T09:05:30"
  }
]
```

### 11.3 檔案安全

- **SemaphoreSlim(1,1):** 確保同一時間只有一個執行緒存取檔案
- **Auto-Create:** 目錄不存在時自動建立
- **Corrupt Recovery:** JSON 解析失敗時回傳空集合（不拋異常）
- **.gitignore:** 兩個 JSON 檔案皆排除在版控之外

---

## 12. 執行緒安全設計

| 元件 | 機制 | 保護範圍 |
|------|------|----------|
| `RiskManager` | `lock` | DailyTradeCount, DailyRealizedLoss |
| `TradingConfiguration` | `lock` | GapConfig, DipConfig, RiskConfig |
| `TradingStateService` | `lock` | 所有 Queue 和狀態欄位 |
| `JsonConfigurationStore` | `SemaphoreSlim` | 檔案 I/O（非同步安全）|
| `JsonTradeJournalStore` | `SemaphoreSlim` | 檔案 I/O（非同步安全）|
| `FugleMarketDataFeed` | `lock` + `Channel<T>` | 訂閱字典 + 行情緩衝 |

**設計原則：**
- 同步操作用 `lock`（短暫鎖定）
- 非同步 I/O 用 `SemaphoreSlim`（支援 `await`）
- 行情管線用 `Channel<T>` Bounded Buffer（背壓控制 + DropOldest）

---

## 12.1 DateTime 一致性政策

系統內部一律使用 **`DateTime.UtcNow`**，僅 UI 展示層（如 `MainLayout.razor` 時鐘、日期選擇器）使用本地時間。

| 位置 | 使用 | 說明 |
|------|------|------|
| `TradeRecord.CreatedAt` | `DateTime.UtcNow` | 紀錄建立時間 |
| `SignalContext.Timestamp` | `DateTime.UtcNow` | 訊號時間戳 |
| `RejectedSignal.Timestamp` | `DateTime.UtcNow` | 拒絕時間戳 |
| `JsonConfigurationStore.LastModified` | `DateTime.UtcNow` | 設定修改時間 |
| `FugleMarketDataFeed` 回退值 | `DateTime.UtcNow` | WebSocket 解析失敗時的 fallback |
| UI 時鐘 / DatePicker | `DateTime.Now` / `DateTime.Today` | 展示層對使用者顯示本地時間 |

---

## 13. 測試架構

### 13.1 測試分佈

```
Total: 116 Tests (All Passing)
│
├── QuantTrading.Core.Tests (94)
│   ├── StrategyA_PreMarketGapTests     (7)   — 策略 A 單元測試
│   ├── StrategyB_IntradayDipTests      (8)   — 策略 B 單元測試
│   ├── RiskManagerTests                (10)  — 風控管理器
│   ├── MarketDataSimulatorTests        (4)   — 合成行情
│   ├── FugleMarketDataFeedTests        (38)  — Fugle WebSocket 整合
│   └── JsonTradeJournalStoreTests      (27)  — 交易日誌持久化 + CSV 匯出
│
└── QuantTrading.E2E.Tests (22)
    ├── WebApiE2ETests                  (8)   — REST API 端對端
    ├── TradingPipelineE2ETests         (5)   — 行情→策略→風控 Pipeline
    └── TradeJournalE2ETests            (9)   — 交易日誌 CRUD + CSV 匯出
```

### 13.2 測試策略

| 類型 | 工具 | 範圍 |
|------|------|------|
| **單元測試** | xUnit + FluentAssertions | 策略邏輯、風控計算、持久化 |
| **E2E Pipeline** | xUnit | 完整行情→訊號流程 |
| **E2E Web API** | WebApplicationFactory | 真實 HTTP 管線 |
| **並行安全** | Task.WhenAll | 多執行緒寫入測試 |
| **邊界案例** | Theory + InlineData | 金額計算、空值處理 |

### 13.3 執行測試

```powershell
# 全部測試
dotnet test

# 只跑單元測試
dotnet test tests/QuantTrading.Core.Tests

# 只跑 E2E
dotnet test tests/QuantTrading.E2E.Tests

# 使用批次檔
run-tests.bat
```

---

## 14. 擴展指南

### 14.1 新增策略

1. **在 `StrategyType` enum 加入新類型**
2. **在 `StrategyEngine` 加入新的 Evaluate 方法**
3. **在 `ProcessTickAsync` / `ProcessBarAsync` 加入路由**
4. **加入對應的 Config record**
5. **撰寫單元測試 + E2E 測試**

### 14.2 新增行情源

1. **實作 `IMarketDataFeed` 介面**
2. **在 Infrastructure 層建立新的 Feed 類別**
3. **建立對應的 DI 擴充方法**
4. **在 `Program.cs` 註冊**

### 14.3 替換持久化

1. **實作 `IConfigurationStore` 或 `ITradeJournalStore`**
2. **建立新的 Store 類別（如 SQLite, PostgreSQL）**
3. **在 `Program.cs` 替換 DI 註冊**

### 14.4 新增 Web 頁面

1. **在 `Components/Pages/` 建立 `.razor` 檔案**
2. **加入 `@page "/route"` 指示詞**
3. **在 `NavMenu.razor` 加入導航連結**
4. **加入對應 CSS 樣式於 `app.css`**

---

## 15. 附錄：介面清單

### 核心介面

```csharp
// 行情資料源
interface IMarketDataFeed {
    event Action<TickData> OnTickReceived;
    event Action<BarData> OnBarClosed;
    void Subscribe(string ticker, MarketDataType dataType);
    void Unsubscribe(string ticker);
    bool IsConnected { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

// 策略引擎
interface IStrategyEngine {
    Task ProcessTickAsync(TickData tick);
    Task ProcessBarAsync(BarData bar);
    event Action<SignalContext> OnSignalGenerated;
    event Action<RejectedSignal> OnSignalRejected;
    void SetReferencePrice(string ticker, decimal refPrice);
}

// 風控管理器
interface IRiskManager {
    (SignalContext? Signal, SignalResult Result) EvaluateSignal(...);
    void RecordRealizedLoss(decimal lossAmount);
    void ResetDaily();
    SignalResult GetCurrentStatus();
    int DailyTradeCount { get; }
    decimal DailyRealizedLoss { get; }
}

// 交易引擎工廠
interface ITradingEngineFactory {
    (IStrategyEngine Engine, IRiskManager RiskManager) Create(
        RiskConfig?, PreMarketGapConfig?, IntradayDipConfig?);
}

// 組態持久化
interface IConfigurationStore {
    Task<TradingConfiguration> LoadAsync();
    Task SaveAsync(TradingConfiguration config);
}

// 交易日誌持久化
interface ITradeJournalStore {
    Task<List<TradeRecord>> GetAllAsync();
    Task<List<TradeRecord>> GetByDateAsync(DateTime date);
    Task AddAsync(TradeRecord record);
    Task UpdateAsync(TradeRecord record);
    Task DeleteAsync(string id);
    Task<string> ExportCsvAsync(DateTime? fromDate, DateTime? toDate);
}
```

---

> **文件維護：** 當新增策略、介面或重大架構變更時，請同步更新本文件。
