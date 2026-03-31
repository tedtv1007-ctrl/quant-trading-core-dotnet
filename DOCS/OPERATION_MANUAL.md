# QuantTrading Core — 功能操作手冊

> **版本:** v3.0 | **更新日期:** 2026-03-31 | **框架:** .NET 10 + Blazor Server  
> **測試覆蓋:** 147 tests (104 Unit + 43 E2E) — 全部通過

---

## 目錄

1. [系統概觀](#1-系統概觀)
2. [Dashboard — 交易儀表板](#2-dashboard--交易儀表板)
3. [Simulation — 多股模擬引擎](#3-simulation--多股模擬引擎)
4. [Signal Log — 訊號與拒絕日誌](#4-signal-log--訊號與拒絕日誌)
5. [Performance Analytics — 績效分析](#5-performance-analytics--績效分析)
6. [System Event Log — 系統事件日誌](#6-system-event-log--系統事件日誌)
7. [Trade Journal — 交易日誌](#7-trade-journal--交易日誌)
8. [Configuration — 策略與風控參數](#8-configuration--策略與風控參數)
9. [REST API 端點](#9-rest-api-端點)
10. [Health Check — 系統健康檢查](#10-health-check--系統健康檢查)
11. [Docker 部署](#11-docker-部署)

---

## 1. 系統概觀

QuantTrading Core 是一套**台灣股市日內交易輔助系統**，採用事件驅動架構，支援兩種量化策略的模擬回測與即時訊號產出。

### 系統架構

```
行情資料 (Tick/Bar)
        │
        ▼
┌─────────────────┐    訊號     ┌─────────────┐    允許/拒絕    ┌───────────────┐
│  Strategy Engine │ ─────────▶ │ Risk Manager │ ────────────▶ │ Signal Output │
│  (策略 A + B)    │            │  (風控閘門)   │               │  (事件通知)    │
└─────────────────┘            └─────────────┘               └───────────────┘
        │                                                           │
        ▼                                                           ▼
┌─────────────────┐                                      ┌─────────────────┐
│  Event Log       │                                      │ Blazor Dashboard │
│  (結構化日誌)    │                                      │ + REST API       │
└─────────────────┘                                      │ + Analytics      │
                                                          └─────────────────┘
```

### 核心模組

| 模組 | 說明 |
|------|------|
| **Strategy Engine** | 處理行情資料，產出交易訊號（Strategy A: 試搓跳空、Strategy B: 盤中低接） |
| **Risk Manager** | 風控閘門，驗證停損、部位大小、每日交易上限 |
| **Liquidity Risk Evaluator** | 流動性風險評估，距跌停 ≤2% 時強制停損 |
| **Trade Statistics Service** | 績效指標計算（P/L、勝率、最大回撤等） |
| **System Event Log** | 結構化系統事件紀錄 |
| **Trade Journal** | 每日實際成交紀錄（CRUD + CSV 匯出） |

---

## 2. Dashboard — 交易儀表板

**路徑:** `/` (首頁)

### 功能說明

Dashboard 是系統的核心監控畫面，提供即時交易狀態的全景視圖。

#### 2.1 全域統計面板

顯示五個關鍵指標卡片：

| 指標 | 說明 | 顏色編碼 |
|------|------|----------|
| **Status** | 模擬狀態 (Running/Idle) | 綠色=運行中 |
| **Watchlist** | 監控中的股票數量 | 藍色 |
| **Today's Trades** | 當日已執行交易 / 上限 | 紫色 |
| **Daily Loss** | 當日累計虧損金額 | 超過 60% 上限轉紅 |
| **Total Signals** | 已產出的訊號總數 | 綠色 |

#### 2.2 個股監控面板 (Per-Ticker Console)

每支監控中的股票會顯示獨立的面板，包含：

- **即時報價** — 最新成交價、漲跌幅百分比
- **走勢迷你圖** — SVG 繪製的 Sparkline
- **多空力量指標** — 基於最近 50 筆 Tick 的內外盤比例
- **訊號列表** — 該標的最近 5 筆交易訊號
- **拒絕列表** — 風控拒絕紀錄

#### 2.3 即時 Tick 表

顯示最近 20 筆全域 Tick 資料，包含時間、標的、價格、成交量，以顏色區分漲跌（紅=外盤/綠=內盤）。

---

## 3. Simulation — 多股模擬引擎

**路徑:** `/simulation`

### 功能說明

模擬引擎支援同時對多支股票執行策略模擬，無需連接真實行情源。

#### 3.1 Watchlist 管理

- **新增股票** — 輸入代號 (如 `2330`) 和參考價後按「Add」
- **快速新增** — 點擊預設按鈕 (台積電、鴻海、聯發科、富邦金)
- **移除股票** — 點擊紅色垃圾桶圖示
- **模擬期間不可修改** — 按鈕會自動禁用

#### 3.2 模擬控制

| 參數 | 說明 | 預設值 |
|------|------|--------|
| **Simulation Date** | 模擬日期 | 今日 |
| **Tick Delay** | Tick 之間的延遲時間 | 100ms |
| **Replay Speed** | 歷史回放加速倍數 | 2x |

| 按鈕 | 說明 |
|------|------|
| **Start All** | 啟動所有監控股票的模擬 |
| **Historical Replay** | 從本地 JSON 檔案回放歷史行情 |
| **Stop** | 停止正在執行的模擬 |
| **Clear** | 清除所有模擬資料 |

#### 3.3 模擬階段

模擬分為兩個階段平行執行：

1. **Phase 1: Pre-Market Gap** (08:30 ~ 09:00) — 試搓階段，監控跳空訊號
2. **Phase 2: Intraday Dip** (09:01 ~ 13:25) — 盤中階段，監控低接反彈訊號

#### 3.4 即時狀態面板

右側面板即時顯示：
- 監控股票數、交易筆數、訊號數、拒絕數
- 各股最新報價
- 最近訊號表 (含策略類型、進場價、停損價、部位大小)

---

## 4. Signal Log — 訊號與拒絕日誌

**路徑:** `/signals`

### 功能說明

完整的訊號紀錄檢視器，支援四種資料類型切換。

#### 4.1 Ticker 過濾器

下拉選單可篩選特定股票的資料，或選「All Tickers」查看全部。

#### 4.2 四種檢視標籤

| 標籤 | 說明 | 欄位 |
|------|------|------|
| **Accepted** | 通過風控的訊號 | 時間、策略、訂單類型、標的、進場價、停損、部位、量比 |
| **Rejected** | 被風控拒絕的訊號 | 時間、拒絕原因、策略、標的 |
| **Ticks** | 原始逐筆成交 | 時間、標的、價格、成交量 |
| **Bars** | K 棒資料 | 時間、標的、OHLC、成交量 |

#### 4.3 拒絕原因代碼

| 代碼 | 說明 |
|------|------|
| `RejectRisk` | 風控規則判定不符（如 Fakeout、弱勢反彈） |
| `RejectMaxTrades` | 當日交易筆數已達上限 |
| `RejectDailyLoss` | 當日累計虧損已達上限 |

---

## 5. Performance Analytics — 績效分析

**路徑:** `/analytics`

### 功能說明

從 Trade Journal 的交易紀錄計算績效指標，提供視覺化的損益分析。  
**靈感來源:** NoFx Dashboard 的即時績效追蹤設計。

#### 5.1 日期範圍篩選

- **手動設定** — From / To 日期選擇器
- **快速篩選** — This Month / This Week / All Time
- **Refresh** — 手動重新計算

#### 5.2 關鍵績效指標

| 指標 | 說明 | 計算方式 |
|------|------|----------|
| **Total P/L** | 已實現總損益 | FIFO 配對法 (Buy → Sell) |
| **Win Rate** | 勝率百分比 | 獲利筆數 / 總配對數 × 100 |
| **Total Trades** | 總交易筆數 | 包含未配對的記錄 |
| **Profit Factor** | 利潤因子 | 平均獲利 / 平均虧損 |
| **Max Drawdown** | 最大回撤 | 累計損益曲線的峰谷差 |

#### 5.3 詳細分析面板

- **Win/Loss Detail** — 平均獲利、平均虧損、買進總額、賣出總額、淨流量
- **Strategy Breakdown** — 各策略使用次數佔比 (進度條顯示)
- **Daily P/L** — 最近 14 天的每日損益柱狀圖（綠=獲利、紅=虧損）

#### 5.4 FIFO 配對說明

系統使用 **先進先出 (FIFO)** 方式配對買賣交易：
- 每筆賣出會與最早的同標的買入配對
- 支援部分平倉（一筆賣出可拆分配對多筆買入）
- 未配對的買入不計入損益

---

## 6. System Event Log — 系統事件日誌

**路徑:** `/events`

### 功能說明

記錄交易系統運行過程中的所有關鍵事件，類似 NoFx 的 AI Decision Log 設計。

#### 6.1 事件層級

| 層級 | 說明 | 顏色 |
|------|------|------|
| **Info** | 一般資訊（訊號產出、模擬啟動等） | 藍色 |
| **Warning** | 警告（風控拒絕、高風險偵測） | 黃色 |
| **Error** | 錯誤（連線中斷、處理失敗） | 紅色 |

#### 6.2 事件類別

| 類別 | 說明 |
|------|------|
| **Signal** | 策略引擎產出的訊號事件 |
| **Risk** | 風控管理器的決策事件 |
| **System** | 系統層級事件（啟動、停止、設定變更） |
| **Simulation** | 模擬引擎的狀態事件 |
| **Config** | 組態變更事件 |

#### 6.3 過濾功能

支援 Level 和 Category 的組合篩選，並顯示事件詳情（如訊號參數、風控原因）。

#### 6.4 容量管理

系統最多保留 **2000 筆** 事件，超過自動淘汰最舊的紀錄。

---

## 7. Trade Journal — 交易日誌

**路徑:** `/trades`

### 功能說明

手動記錄每日實際成交，支援完整 CRUD 操作。

#### 7.1 新增交易

| 欄位 | 說明 | 必填 |
|------|------|------|
| **Trade Date** | 交易日期 | ✔ |
| **Ticker** | 股票代號 (如 2330) | ✔ |
| **Direction** | Buy (買進) / Sell (賣出) | ✔ |
| **Price** | 成交價格 | ✔ |
| **Quantity** | 成交股數 | ✔ |
| **Strategy** | 使用的策略 (可選) | — |
| **Note** | 備註 | — |

#### 7.2 交易歷史表

- **日期篩選** — 選擇特定日期或點「All」查看全部
- **編輯** — 藍色鉛筆圖示，修改後點 Update 儲存
- **刪除** — 紅色垃圾桶圖示

#### 7.3 每日摘要

左側面板自動計算當日：
- 交易筆數
- 買進總額
- 賣出總額
- 淨損益 (Sell - Buy)

#### 7.4 CSV 匯出

點擊右上「CSV」按鈕匯出交易紀錄，檔案包含 BOM 標頭，支援 Excel 直接開啟中文。

---

## 8. Configuration — 策略與風控參數

**路徑:** `/configuration`

### 功能說明

編輯策略參數和風控設定，變更會即時寫入本地 JSON 檔案。

#### 8.1 Strategy A: Pre-Market Gap (試搓跳空策略)

| 參數 | 說明 | 預設值 |
|------|------|--------|
| **Monitor Start** | 試搓監控開始時間 | 08:30 |
| **Monitor End** | 試搓監控結束時間 | 08:59:55 |
| **Execution Time** | 開盤送單時間 | 09:00:00 |
| **Gap Strength %** | 跳空強度門檻 | 1% |
| **Fakeout Pullback %** | 假突破回檔容許幅度 | 0.5% |
| **Stop Loss Offset %** | 停損距離比率 | 1% |

#### 8.2 Strategy B: Open-Base (開盤價基準策略)

| 參數 | 說明 | 預設值 |
|------|------|--------|
| **Active Start** | 策略啟動時間 | 09:01 |
| **Active End** | 策略結束時間 | 13:25 |
| **Trigger Offset %** | 買賣觸發偏移 | 1% |
| **Dip Threshold %** | 低接門檻 (相對 VWAP) | 2% |
| **Volume Spike ×** | 爆量倍數門檻 | 2.0x |
| **Stop Loss Offset %** | 停損距離比率 | 1% |

#### 8.3 Risk Management (風控參數)

| 參數 | 說明 | 預設值 |
|------|------|--------|
| **Risk Per Trade** | 單筆最大停損金額 | $1,100 TWD |
| **Max Daily Loss** | 單日最大虧損金額 | $5,000 TWD |
| **Max Daily Trades** | 單日最大交易筆數 | 5 |

#### 8.4 部位計算公式

```
Position Size = Risk Per Trade / |Entry Price - Stop Loss Price|
```

範例：Entry=600, StopLoss=594 → Size = 1100 / 6 = **183 股**

#### 8.5 時間軸視覺化

底部時間軸圖形化顯示 Strategy A 和 B 的活躍時段，幫助理解策略覆蓋範圍。

---

## 9. REST API 端點

### 基本路徑: `/api/trading`

| Method | Path | 說明 |
|--------|------|------|
| GET | `/watchlist` | 取得監控清單 |
| POST | `/watchlist` | 新增監控股票 `{ticker, refPrice}` |
| DELETE | `/watchlist/{ticker}` | 移除監控股票 |
| GET | `/signals?ticker=XXX` | 取得訊號 (可篩選) |
| GET | `/rejections?ticker=XXX` | 取得被拒絕訊號 |
| GET | `/status` | 取得風控狀態快照 |
| POST | `/simulate` | 啟動多股模擬 (回傳 202) |
| POST | `/stop` | 停止模擬 |
| GET | `/ticks?ticker=XXX` | 取得最近 Ticks |
| GET | `/bars?ticker=XXX` | 取得最近 Bars |
| GET | `/journal/export?from=&to=` | 匯出 CSV |
| GET | `/analytics?from=&to=` | 績效分析摘要 |
| GET | `/events?count=&level=&category=` | 系統事件日誌 |

### Health Check

| Method | Path | 說明 |
|--------|------|------|
| GET | `/api/health` | 系統健康檢查 (回傳 "Healthy") |

---

## 10. Health Check — 系統健康檢查

**端點:** `GET /api/health`

回傳 HTTP 200 與字串 `Healthy`，可用於：
- Docker 容器健康檢查
- 監控系統 (如 Uptime Robot, Prometheus)
- 負載均衡器健康探測

---

## 11. Docker 部署

### 使用 Docker Compose

```bash
docker compose up -d
```

系統將在 `http://localhost:3000` 啟動。

### 環境變數

| 變數 | 說明 | 預設值 |
|------|------|--------|
| `ASPNETCORE_ENVIRONMENT` | 運行環境 | Production |
| `ASPNETCORE_URLS` | 監聽埠 | http://+:8080 |

### 資料持久化

Docker volume `trading-data` 掛載至 `/app/Data`，保存：
- `trading-config.json` — 策略與風控組態
- `trade-journal.json` — 交易日誌紀錄

### 健康檢查

Docker Compose 自動設定 30 秒間隔的健康檢查，探測 `/api/health` 端點。
