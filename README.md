# QuantTrading Core

> 台灣股市日內交易輔助系統 — C# .NET 8 + Blazor Server

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor Server](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor)](https://learn.microsoft.com/aspnet/core/blazor/)
[![Tests](https://img.shields.io/badge/tests-116%20passed-22c55e)]()
[![License](https://img.shields.io/badge/license-MIT-blue)]()

---

## 📖 簡介

QuantTrading Core 是一套**事件驅動架構（EDA）**的台股日內交易輔助系統，具備：

- 🎯 **兩套量化策略** — 盤前試搓跳空 + 盤中低接反彈
- 🛡️ **完整風控機制** — 單筆停損、每日上限、累計虧損閘門
- 📊 **暗色主題交易儀表板** — Blazor Server 即時 UI
- 🔌 **Fugle 即時行情** — WebSocket 串流（選配）
- 📓 **交易日誌** — 手動記錄實際成交 + CSV 匯出
- 🧪 **116 個自動化測試** — 單元測試 + E2E 全通過

---

## 🏗️ 系統架構

### 分層架構

```
┌──────────────────────────────────────┐
│          QuantTrading.Web            │  ← 展示層 (Blazor Server + REST API)
│    Dashboard / Simulation / API      │
└──────────────┬───────────────────────┘
               │ references
               ▼
┌──────────────────────────────────────┐
│      QuantTrading.Infrastructure     │  ← 基礎設施層 (Fugle WebSocket, JSON Store)
│    WebSocket / File I/O / DI         │
└──────────────┬───────────────────────┘
               │ references
               ▼
┌──────────────────────────────────────┐
│          QuantTrading.Core           │  ← 核心層 (零外部依賴)
│   Models / Interfaces / Strategy     │
└──────────────────────────────────────┘
```

**核心原則：** `QuantTrading.Core` 不引用任何 NuGet 套件，確保策略邏輯可在任何環境測試與重用。

### 資料流

```
行情資料 (Tick/Bar)
      │
      ▼
┌──────────────┐    訊號     ┌─────────────┐   Accept/Reject   ┌───────────────┐
│ StrategyEngine│ ─────────▶ │ RiskManager  │ ────────────────▶ │ Signal Output │
│ (策略 A + B)  │            │  (風控閘門)   │                  │  (事件通知)    │
└──────────────┘            └─────────────┘                   └───────────────┘
                                                                      │
                                                                      ▼
                                                            ┌─────────────────┐
                                                            │ Blazor Dashboard │
                                                            │   + REST API     │
                                                            └─────────────────┘
```

### 專案結構

```
quant-trading-core-dotnet/
│
├── build.bat / run-web.bat / run-tests.bat    # 批次檔
├── quant-trading-core-dotnet.sln              # Solution
│
├── src/
│   ├── QuantTrading.Core/                     # 核心層
│   │   ├── Models.cs                          #   領域模型 & Enums
│   │   ├── Interfaces.cs                      #   抽象介面
│   │   ├── StrategyEngine.cs                  #   策略引擎
│   │   ├── RiskManager.cs                     #   風控管理器
│   │   └── TradingEngineFactory.cs            #   引擎工廠
│   │
│   ├── QuantTrading.Infrastructure/           # 基礎設施層
│   │   ├── Configuration/                     #   JSON 持久化
│   │   └── Fugle/                             #   Fugle WebSocket
│   │
│   └── QuantTrading.Web/                      # 展示層
│       ├── Components/Pages/                  #   6 個 Blazor 頁面
│       ├── Services/                          #   背景服務 & API
│       └── Data/                              #   JSON 資料檔
│
├── tests/
│   ├── QuantTrading.Core.Tests/               # 94 個單元測試
│   └── QuantTrading.E2E.Tests/                # 22 個 E2E 測試
│
└── DOCS/                                      # 文件
    ├── ARCHITECTURE.md                        #   系統架構詳解
    ├── USER_GUIDE.md                          #   完整使用手冊
    ├── USER_MANUAL.md                         #   使用手冊 (v2)
    ├── STARTUP.md                             #   啟動手冊
    └── STRATEGY_SPEC.md                       #   策略規格書
```

---

## ⚡ 快速開始

### 環境需求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 瀏覽器（Chrome / Edge / Firefox）

### 三步驟啟動

```powershell
# 1. 複製設定檔
cd src\QuantTrading.Web
copy appsettings.template.json appsettings.json

# 2. 還原套件
cd ..\..
dotnet restore

# 3. 啟動
.\run-web.bat
```

瀏覽器開啟 **https://localhost:7217** 即可使用。

> 💡 首次使用 HTTPS 若出現憑證警告，執行 `dotnet dev-certs https --trust` 或改用 `http://localhost:5148`

---

## 📈 交易策略

### Strategy A: 盤前試搓 (Pre-Market Gap)

| 項目 | 說明 |
|------|------|
| **時段** | 08:30 – 09:00 |
| **進場條件** | 試搓價 > 昨收 × 1.01，且無假突破 |
| **訊號類型** | Market Buy（市價買進）|
| **目標** | 捕捉開盤強勢跳空 |

### Strategy B: 盤中低接 (Intraday Dip)

| 項目 | 說明 |
|------|------|
| **時段** | 09:01 – 13:25 |
| **進場條件** | 價格 < VWAP − 2%，量能 ≥ 均量 × 3，止跌反彈確認 |
| **訊號類型** | Limit Buy（限價買進）|
| **目標** | 急跌放量後的反彈進場 |

### 風控規則

| 規則 | 預設值 |
|------|--------|
| 單筆停損 | 1,100 TWD |
| 每日交易上限 | 5 筆 |
| 每日累計虧損上限 | 5,000 TWD |
| 部位計算 | `Position = 1,100 / (進場價 − 停損價)` |

---

## 📝 發展現況 (Development Status)

- [x] **Phase 1**: 策略邏輯實作 (Strategy A & B)
- [x] **Phase 2**: 風控模組 (Risk Management)
- [x] **Phase 3**: gRPC / WebSocket 介面定義
- [x] **Phase 4**: Worker Service 背景執行核心
- [x] **Phase 5**: Blazor Web Dashboard 介面
- [x] **Phase 6**: 多股票同步模擬引擎
- [ ] **Phase 7**: 實盤交易 API 串接

---

## 🖥️ 功能頁面

| 頁面 | 路徑 | 功能 |
|------|------|------|
| **Dashboard** | `/` | 系統狀態總覽、即時價格、最近訊號 |
| **Simulation** | `/simulation` | 加入監控股票、啟動/停止模擬 |
| **Signal Log** | `/signals` | 訊號日誌（Accepted / Rejected / Ticks / Bars）|
| **Trade Journal** | `/trades` | 手動交易紀錄 CRUD + CSV 匯出 |
| **Configuration** | `/configuration` | 策略與風控參數即時編輯 |

---

## 🔌 REST API

基礎路徑：`/api/trading`

```
GET    /api/trading/watchlist              # 取得監控清單
POST   /api/trading/watchlist              # 新增股票
DELETE /api/trading/watchlist/{ticker}      # 移除股票

POST   /api/trading/simulate              # 啟動模擬
POST   /api/trading/stop                  # 停止模擬

GET    /api/trading/signals               # 策略訊號
GET    /api/trading/rejections            # 風控拒絕紀錄
GET    /api/trading/status                # 系統狀態
GET    /api/trading/ticks                 # 近期 Tick
GET    /api/trading/bars                  # 近期 Bar

GET    /api/trading/journal/export        # 匯出交易日誌 CSV
```

---

## 🧰 技術棧

| 技術 | 說明 |
|------|------|
| C# 12 / .NET 8 | LTS 高效能運行時 |
| Blazor Server | 即時雙向 SignalR 通訊 |
| ASP.NET Core Minimal API | 輕量 REST Endpoints |
| Fugle WebSocket v1.0 | 台股即時行情串流（選配）|
| Polly 8.x | Exponential Backoff 重連 |
| JSON File Store | 輕量無 DB 依賴持久化 |
| xUnit + FluentAssertions | BDD 風格測試 |
| Bootstrap 5 | 暗色交易終端主題 |

---

## 🧪 測試

```powershell
# 全部測試（116 個）
dotnet test

# 單元測試
dotnet test tests/QuantTrading.Core.Tests

# E2E 測試
dotnet test tests/QuantTrading.E2E.Tests

# 使用批次檔
.\run-tests.bat
```

---

## 📚 文件

| 文件 | 說明 |
|------|------|
| [ARCHITECTURE.md](DOCS/ARCHITECTURE.md) | 系統架構詳解（15 章節）|
| [USER_GUIDE.md](DOCS/USER_GUIDE.md) | 完整使用手冊 |
| [STARTUP.md](DOCS/STARTUP.md) | 啟動 & 設定指南 |
| [STRATEGY_SPEC.md](DOCS/STRATEGY_SPEC.md) | 策略規格書 |

---

## 📜 License

MIT License
