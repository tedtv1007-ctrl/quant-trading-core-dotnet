# QuantTrading Core — 完整使用手冊

> **版本:** v2.1 | **最後更新:** 2026-03-03  
> **系統:** .NET 8 + Blazor Server | **測試:** 116 tests 全部通過

---

## 目錄

1. [系統簡介](#1-系統簡介)
2. [安裝與啟動](#2-安裝與啟動)
3. [Dashboard — 即時總覽](#3-dashboard--即時總覽)
4. [Simulation — 模擬操控台](#4-simulation--模擬操控台)
5. [Signal Log — 訊號日誌](#5-signal-log--訊號日誌)
6. [Trade Journal — 交易日誌](#6-trade-journal--交易日誌)
7. [Configuration — 參數設定](#7-configuration--參數設定)
8. [策略原理詳解](#8-策略原理詳解)
9. [風控機制detailed](#9-風控機制詳解)
10. [REST API 完整參考](#10-rest-api-完整參考)
11. [Fugle 即時行情（進階）](#11-fugle-即時行情進階)
12. [系統架構與擴展](#12-系統架構與擴展)
13. [常見問題 FAQ](#13-常見問題-faq)
14. [附錄](#14-附錄)

---

## 1. 系統簡介

### 1.1 什麼是 QuantTrading Core？

QuantTrading Core 是一套專為**台灣股市**設計的日內交易輔助系統。它能夠：

- **模擬完整交易日**：從盤前試搓（08:30）到盤中收盤（13:25）
- **產出交易訊號**：兩套量化策略自動偵測進場時機
- **風控把關**：每筆訊號經過三道風控閘門驗證
- **即時 Dashboard**：暗色主題的專業交易終端 UI
- **記錄實際交易**：手動記錄每日真實成交，支援 CSV 匯出

### 1.2 核心概念

```
┌─────────────────────────────────────────────────────────┐
│                    QuantTrading Core                      │
│                                                           │
│  行情輸入          策略判定          風控驗證    訊號輸出  │
│  ─────────  ──▶  ──────────  ──▶  ──────────  ──▶  ───── │
│  Tick / Bar       Strategy A/B    RiskManager    Signal   │
│  (模擬或即時)     (跳空/低接)     (停損/上限)    (Accept  │
│                                                  /Reject) │
└─────────────────────────────────────────────────────────┘
```

### 1.3 兩種運作模式

| 模式 | 說明 | 適用場景 |
|------|------|----------|
| **模擬模式** | 內建行情模擬器產生合成 Tick/Bar 資料 | 學習系統、調參、回測驗證 |
| **即時模式** | 接收 Fugle WebSocket 真實行情串流 | 實盤輔助（需付費 API）|

> 📌 **建議：** 先用模擬模式熟悉系統，再考慮接入即時行情。

---

## 2. 安裝與啟動

### 2.1 環境需求

| 項目 | 最低需求 |
|------|----------|
| 作業系統 | Windows 10+ / macOS / Linux |
| 執行環境 | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 瀏覽器 | Chrome / Edge / Firefox（需支援 WebSocket）|
| IDE（選用）| Visual Studio 2022 / VS Code / Rider |

驗證安裝：

```powershell
dotnet --version
# 應顯示 8.x.x
```

### 2.2 初次設定

#### Step 1：複製設定檔

```powershell
cd src\QuantTrading.Web
copy appsettings.template.json appsettings.json
```

#### Step 2：編輯設定

開啟 `appsettings.json`，填入 Fugle API Token：

```json
{
  "Fugle": {
    "ApiToken": "YOUR_TOKEN_HERE"
  }
}
```

> 💡 **注意：** 如果只使用模擬模式（不連 Fugle），填入任意非空字串即可，例如 `"demo"`。

#### Step 3：還原套件

```powershell
cd ..\..
dotnet restore
```

### 2.3 啟動方式

#### 方式一：批次檔（最簡單）

```powershell
.\run-web.bat
```

#### 方式二：命令列

```powershell
dotnet run --project src/QuantTrading.Web
```

#### 方式三：Visual Studio

1. 開啟 `quant-trading-core-dotnet.sln`
2. 設定 **QuantTrading.Web** 為啟動專案
3. 按 `F5` 啟動

### 2.4 存取系統

啟動後開啟瀏覽器：

| 協定 | 網址 |
|------|------|
| HTTPS | **https://localhost:7217** |
| HTTP | http://localhost:5148 |

> ⚠️ 首次使用 HTTPS 若出現憑證警告，執行 `dotnet dev-certs https --trust` 或改用 HTTP。

---

## 3. Dashboard — 即時總覽

**路徑：** `/`（首頁）

Dashboard 是系統的主頁面，提供一目了然的即時狀態總覽。

### 3.1 全域狀態卡片

頁面頂部顯示 5 張狀態卡片：

| 卡片 | 說明 | 示例值 |
|------|------|--------|
| 📡 **Status** | 目前模擬狀態 | `IDLE` / `RUNNING` / `COMPLETED` |
| 📋 **Watchlist** | 監控清單股票數 | `3 stocks` |
| 📊 **Today's Trades** | 今日成交筆數 / 上限 | `2 / 5` |
| 💰 **Daily Loss** | 今日累計虧損 | `$1,100` |
| ⚡ **Signals** | 總產出訊號數 | `5` |

### 3.2 每股即時卡片

對監控清單中每檔股票顯示：
- 股票代號 & 最新價格
- 漲跌幅百分比（🟢 綠漲 🔴 紅跌）
- 該股的訊號數 / 被拒絕數

### 3.3 訊號 & 行情表格

| 表格 | 最近筆數 | 主要欄位 |
|------|---------|----------|
| **Recent Signals** | 15 筆 | 時間、策略、買單類型、代號、進場價、停損價、部位、量比 |
| **Rejected Signals** | 10 筆 | 時間、拒絕原因、代號 |
| **Recent Ticks** | 30 筆 | 時間、代號、價格、量 |

> 💡 Dashboard 在模擬運行中會自動刷新，即時顯示新產出的訊號。

---

## 4. Simulation — 模擬操控台

**路徑：** `/simulation`

模擬操控台讓你模擬完整的一日交易流程，從盤前試搓到盤中收盤。

### 4.1 完整操作流程

```
Step 1              Step 2            Step 3         Step 4
加入監控股票  ──▶  設定模擬參數  ──▶  啟動模擬  ──▶  觀察結果
                                                    
  [+ ADD]           Date: 今天       [▶ Start]     Dashboard
  or                Delay: 100ms                   Signal Log
  [Quick Add]
```

### 4.2 加入監控股票

#### 方式一：手動輸入

1. 在 **Ticker** 欄位輸入股票代號（例如 `2330`）
2. 在 **Ref Price** 欄位輸入昨日收盤參考價（例如 `600`）
3. 點擊 **[+ ADD]** 按鈕

#### 方式二：快速新增

點擊預設按鈕，一鍵加入：

| 按鈕 | 股票 | 預設參考價 |
|------|------|-----------|
| 2330 | 台積電 | 600 |
| 2317 | 鴻海 | 100 |
| 2454 | 聯發科 | 950 |
| 2881 | 富邦金 | 70 |

### 4.3 監控清單管理

- 每檔股票顯示在表格中，包含代號、參考價、狀態
- 點擊 🗑️ 按鈕可移除該股票
- 模擬運行中會即時更新每檔股票的處理狀態

### 4.4 模擬參數

| 參數 | 說明 | 預設值 | 建議 |
|------|------|--------|------|
| **Simulation Date** | 模擬的交易日日期 | 今天 | 可選任意日期 |
| **Tick Delay** | 每筆 Tick 之間的間隔 | 100ms | 快速測試用 10ms，觀察用 200-500ms |

### 4.5 模擬控制按鈕

| 按鈕 | 功能 | 何時使用 |
|------|------|----------|
| ▶ **Start** | 啟動模擬 | 加入股票後，點擊開始 |
| ⏹ **Stop** | 停止模擬 | 想中途停止時 |
| 🗑️ **Clear** | 清除資料 | 清空所有訊號、Tick、Bar 資料 |

### 4.6 模擬兩階段

模擬會依序執行兩個階段，所有監控股票**同步平行**處理：

| 階段 | 模擬時間 | 產出資料 | 對應策略 |
|------|----------|----------|----------|
| **Phase 1: 盤前** | 08:30 – 09:00 | Tick 資料 | Strategy A 判定 |
| **Phase 2: 盤中** | 09:01 – 13:25 | Bar + Tick 資料 | Strategy B 判定 |

> 📌 **重要：** 多檔股票共用同一個 RiskManager，因此「每日 5 筆上限」是所有股票合計。

### 4.7 即時狀態面板

模擬運行中，右側面板即時顯示：
- 監控股票數 / 今日交易數 / 訊號數 / 被拒絕數
- 每檔股票最新價格
- 最近產出的訊號列表

---

## 5. Signal Log — 訊號日誌

**路徑：** `/signals`

完整的訊號與行情資料檢視頁面。

### 5.1 篩選功能

- **Ticker Filter** 下拉選單：選擇特定股票，或「All Tickers」顯示全部

### 5.2 四個分頁

| Tab | 說明 | 關鍵欄位 |
|-----|------|----------|
| ⚡ **Accepted Signals** | 通過風控的交易訊號 | 策略、買單類型、代號、進場價、停損價、部位、量比 |
| 🚫 **Rejected** | 被風控拒絕的訊號 | 拒絕原因、策略、代號 |
| 📈 **Ticks** | 逐筆 Tick 行情 | 代號、價格、成交量、時間 |
| 📊 **Bars** | 1 分鐘 K 棒 | 代號、開高低收量、時間 |

每個 Tab 標題旁有數量 Badge，方便快速判讀有多少筆紀錄。

### 5.3 訊號欄位說明

| 欄位 | 說明 |
|------|------|
| **Strategy** | `OpenGap`（策略 A）或 `IntradayDip`（策略 B）|
| **Order Type** | `MarketBuy`（市價買）或 `LimitBuy`（限價買）|
| **Entry Price** | 建議進場價格 |
| **StopLoss** | 停損價格 |
| **Position Size** | 建議部位大小（股數）|
| **Volume Ratio** | 量比（Strategy B 專用，≥ 3.0 觸發）|

### 5.4 拒絕原因對照

| 原因 | 說明 | 處理方式 |
|------|------|----------|
| `RejectMaxTrades` | 今日交易已達上限（預設 5 筆）| 等待隔日或調整上限設定 |
| `RejectDailyLoss` | 今日累計虧損已達上限 | 今日不再交易 |
| `RejectRisk` | 停損價異常（≥ 進場價）| 調整停損參數 |

---

## 6. Trade Journal — 交易日誌

**路徑：** `/trades`

手動記錄每日**實際成交**的交易紀錄，與模擬系統分開。

### 6.1 適用場景

- ✅ 記錄你在券商實際下的單
- ✅ 追蹤買賣金額與損益
- ✅ 標記使用的策略
- ✅ 日後回顧分析交易紀錄

### 6.2 新增交易紀錄

左側「New Trade」表單：

| 欄位 | 說明 | 必填 | 範例 |
|------|------|------|------|
| **Date** | 交易日期 | ✅ | 2026-03-01 |
| **Ticker** | 股票代號 | ✅ | 2330 |
| **Direction** | 買 / 賣 | ✅ | Buy / Sell |
| **Price** | 成交價格 | ✅ (> 0) | 612.00 |
| **Quantity** | 成交股數 | ✅ (> 0) | 1000 |
| **Strategy** | 使用策略 | 選填 | Strategy A / B / 手動 / 其他 |
| **Note** | 備註 | 選填 | 盤前跳空買進 |

填完後點擊 **「💾 Save Trade」** 儲存。

### 6.3 每日摘要

左側「📊 Day Summary」卡片自動統計所選日期：

| 指標 | 說明 |
|------|------|
| **Trades** | 交易筆數 |
| **Buy Total** | 買進總金額 |
| **Sell Total** | 賣出總金額 |
| **Net** | 淨額 = 賣出 − 買進（正數為淨流入）|

### 6.4 交易歷史表格

右側表格顯示所有紀錄：

**篩選方式：**
- 選擇特定日期 → 只顯示該日紀錄
- 點擊 **「📋 All」** 按鈕 → 顯示全部

**表格欄位：**

| 欄位 | 說明 |
|------|------|
| Date | 交易日期 |
| Ticker | 股票代號 |
| Dir | 方向（🟢 Buy / 🔴 Sell）|
| Price | 成交價 |
| Qty | 股數 |
| Amount | 金額 = Price × Qty |
| Strategy | 策略 |
| Note | 備註 |
| Actions | ✏️ 編輯 / 🗑️ 刪除 |

### 6.5 編輯、刪除、取消

| 操作 | 步驟 |
|------|------|
| **編輯** | 點擊 ✏️ → 左側表單載入資料 → 修改 → 「💾 Update Trade」|
| **刪除** | 點擊 🗑️ → 直接刪除 |
| **取消編輯** | 點擊「❌ Cancel」→ 回到新增模式 |

### 6.6 匯出 CSV

1. 點擊 **「📥 Export CSV」** 按鈕
2. 系統根據目前篩選範圍自動匯出：
   - 「All」模式 → 匯出全部
   - 特定日期 → 匯出該日
3. 瀏覽器自動下載 `trade-journal-YYYYMMDD-HHmmss.csv`

CSV 欄位：`Id, TradeDate, Ticker, Direction, Price, Quantity, Amount, Strategy, Note, CreatedAt`

> 📌 CSV 使用 UTF-8 BOM 編碼，Excel 可直接開啟，中文不會亂碼。

也可透過 API 匯出：
```
GET /api/trading/journal/export?from=2026-01-01&to=2026-03-31
```

### 6.7 資料儲存

所有交易紀錄儲存在 `Data/trade-journal.json`，重啟系統後資料仍在。

---

## 7. Configuration — 參數設定

**路徑：** `/configuration`

所有策略和風控參數都可以在此頁面即時修改並儲存。

### 7.1 Strategy A 參數（盤前試搓）

| 參數 | 說明 | 預設值 | 格式 |
|------|------|--------|------|
| **Monitor Start** | 開始監測時間 | 08:30:00 | HH:mm:ss |
| **Monitor End** | 判定截止時間 | 08:59:55 | HH:mm:ss |
| **Execution Time** | 下單執行時間 | 09:00:00 | HH:mm:ss |
| **Gap Strength %** | 跳空強度門檻 | 1.0% | 百分比 |
| **Fakeout Pullback %** | 假突破拉回門檻 | 0.5% | 百分比 |
| **Stop Loss Offset %** | 停損偏移 | 1.0% | 百分比 |

### 7.2 Strategy B 參數（盤中低接）

| 參數 | 說明 | 預設值 | 格式 |
|------|------|--------|------|
| **Active Start** | 策略啟動時間 | 09:01:00 | HH:mm:ss |
| **Active End** | 策略結束時間 | 13:25:00 | HH:mm:ss |
| **Dip Threshold %** | 低接門檻（低於 VWAP 幾 %）| 2.0% | 百分比 |
| **Volume Spike ×** | 量能倍數門檻 | 3.0× | 倍數 |
| **Volume Lookback** | 量能回顧 K 棒數 | 5 bar | 整數 |
| **Stop Loss Offset %** | 停損偏移 | 1.0% | 百分比 |

### 7.3 Risk Management 參數

| 參數 | 說明 | 預設值 |
|------|------|--------|
| **Risk Per Trade** | 單筆最大風險金額 | 1,100 TWD |
| **Max Daily Loss** | 每日最大累計虧損 | 5,000 TWD |
| **Max Daily Trades** | 每日最大交易筆數 | 5 |

### 7.4 部位計算公式

頁面會即時顯示部位計算公式：

```
Position Size = Risk Per Trade / (Entry Price − Stop Loss Price)

範例：1,100 / (600 − 594) = 183 股
```

### 7.5 策略時間軸

頁面底部有一條視覺化時間軸：

```
08:30         08:59:55   09:00  09:01                           13:25
  ├── Strategy A 監測 ──┤ 下單 ├──── Strategy B 活躍 ──────────┤
```

### 7.6 儲存 & 重置

| 按鈕 | 功能 |
|------|------|
| 💾 **Save All** | 所有參數寫入 `Data/trading-config.json`，下次啟動自動載入 |
| 🔄 **Reset Defaults** | 恢復所有參數為系統預設值 |

> ⚠️ 儲存後，需啟動新的模擬才會生效。正在執行的模擬不受影響。

---

## 8. 策略原理詳解

### 8.1 Strategy A: 盤前試搓（Pre-Market Gap）

**目標：** 捕捉 09:00 開盤的強勢跳空訊號

**時間軸：**

```
08:30                                    08:59:55           09:00
  │                                         │                 │
  │   試搓 Tick 持續流入                    │                 │
  │   追蹤最高試搓價 (SimHigh)              │                 │
  │   偵測急跌假突破 (Fakeout)              │                 │
  │                                         │                 │
  │                                    判定時刻:              │
  │                                    ✅ 最新價 > 昨收×1.01  │
  │                                    ✅ 無假突破偵測        │
  │                                         │         ──▶ Market Buy!
  └─────────────────────────────────────────┘                 │
```

**判定條件：**

1. **跳空強度：** 最新試搓價 > 昨日收盤價 × (1 + GapStrength%)
2. **無假突破：** 在監測期間內，最高試搓價未出現超過 FakeoutPullback% 的急跌
3. **時間窗口：** 於 08:59:55 時進行最終判定

**訊號輸出：** `MarketBuy`（市價買進）

**適用場景：**
- 外資、投信大量買超造成的盤前強勢
- 利多消息面推升的跳空走勢

### 8.2 Strategy B: 盤中低接反彈（Intraday Dip）

**目標：** 在急跌放量後的反彈點精準進場

**運作原理：**

```
價格
 │
 │  ════════ VWAP ════════
 │
 │  ──── VWAP − 2% ────── (低接門檻線)
 │               ╲
 │                ╲  爆量急跌
 │                 ╲ (量能 ≥ 3× 均量)
 │                  * ← 低接確認
 │                 / 
 │                /  止跌反彈
 │               /   (下一筆 Tick 價格上升)
 │              * ← Limit Buy 訊號！
 │─────────────────────────────── 時間
        09:01                  13:25
```

**三重確認條件：**

1. **量能放大（Bar 層級）：** 當前 1 分 K 量 ≥ 最近 5 根平均量 × 3.0
2. **低接確認（Tick 層級）：** 價格 < VWAP × (1 − DipThreshold%)
3. **止跌反彈（Tick 層級）：** 下一筆 Tick 價格 > 上一筆（確認反彈）

**訊號輸出：** `LimitBuy`（限價買進）

**適用場景：**
- 大盤急殺但個股基本面無虞的超跌
- 法人調節持股時的恐慌性殺盤反彈

### 8.3 VWAP 計算

策略 B 使用的 VWAP（成交量加權平均價格）計算公式：

```
           Σ (TypicalPrice_i × Volume_i)
VWAP = ──────────────────────────────────
               Σ Volume_i

其中 TypicalPrice = (High + Low + Close) / 3
```

---

## 9. 風控機制詳解

### 9.1 三道防線

每個策略訊號產出時，都必須通過 RiskManager 的三道驗證：

```
   策略訊號產生
        │
   ┌────▼─────┐
   │  防線 1   │  今日交易 < 5 筆？
   │  交易次數 │  ✅ 通過 → 繼續
   └────┬─────┘  ❌ → RejectMaxTrades
        │
   ┌────▼─────┐
   │  防線 2   │  累計虧損 < 5,000 TWD？
   │  累計虧損 │  ✅ 通過 → 繼續
   └────┬─────┘  ❌ → RejectDailyLoss
        │
   ┌────▼─────┐
   │  防線 3   │  停損價 < 進場價？
   │  部位計算 │  ✅ → 計算部位 → Accept
   └────┬─────┘  ❌ → RejectRisk
        │
   ✅ 產出 SignalContext
```

### 9.2 部位計算公式

```
Position Size = RiskPerTrade / (EntryPrice − StopLossPrice)
```

### 9.3 實際範例

| 股票 | 進場價 | 停損價 (1%) | 價差 | 部位大小 |
|------|--------|------------|------|----------|
| 2330 台積電 | 612 | 605.88 | 6.12 | **179 股** |
| 2317 鴻海 | 103 | 101.97 | 1.03 | **1,068 股** |
| 2454 聯發科 | 960 | 950.40 | 9.60 | **114 股** |
| 2881 富邦金 | 72 | 71.28 | 0.72 | **1,527 股** |

> 📌 **安全閥：** 單筆部位上限 999,000 股，防止極小價差產生天量。

### 9.4 每日重置

所有風控計數器（交易次數、累計虧損）在每次新模擬開始時自動重置。

---

## 10. REST API 完整參考

基礎 URL：`https://localhost:7217/api/trading`

### 10.1 監控清單

#### 取得監控清單

```
GET /api/trading/watchlist
```

回應範例：
```json
[
  { "ticker": "2330", "refPrice": 600, "status": "Ready", "isRunning": false },
  { "ticker": "2317", "refPrice": 100, "status": "Ready", "isRunning": false }
]
```

#### 新增股票

```
POST /api/trading/watchlist
Content-Type: application/json

{ "ticker": "2330", "refPrice": 600 }
```

#### 移除股票

```
DELETE /api/trading/watchlist/2330
```

### 10.2 模擬控制

#### 啟動模擬

```
POST /api/trading/simulate
Content-Type: application/json

{
  "ticker": "2330",
  "refPrice": 600,
  "simulationDate": "2026-03-01",
  "tickDelayMs": 50
}
```

#### 停止模擬

```
POST /api/trading/stop
```

### 10.3 資料查詢

#### 系統狀態

```
GET /api/trading/status
```

回應範例：
```json
{
  "isSimulationRunning": true,
  "simulationStatus": "Running",
  "dailyTradeCount": 2,
  "dailyRealizedLoss": 0,
  "watchlistCount": 3,
  "activeTickers": ["2317", "2330", "2454"]
}
```

#### 策略訊號

```
GET /api/trading/signals
GET /api/trading/signals?ticker=2330
```

#### 風控拒絕紀錄

```
GET /api/trading/rejections
GET /api/trading/rejections?ticker=2330
```

#### Tick / Bar 資料

```
GET /api/trading/ticks?ticker=2330
GET /api/trading/bars?ticker=2330
```

#### 匯出交易日誌

```
GET /api/trading/journal/export
GET /api/trading/journal/export?from=2026-01-01&to=2026-03-31
```

### 10.4 API 快速測試

```powershell
# 1. 加入股票
curl -X POST https://localhost:7217/api/trading/watchlist `
  -H "Content-Type: application/json" `
  -d '{"ticker":"2330","refPrice":600}' -k

# 2. 啟動模擬
curl -X POST https://localhost:7217/api/trading/simulate `
  -H "Content-Type: application/json" `
  -d '{"ticker":"2330","refPrice":600,"tickDelayMs":10}' -k

# 3. 等待數秒
Start-Sleep -Seconds 5

# 4. 查看狀態
curl https://localhost:7217/api/trading/status -k

# 5. 查看訊號
curl https://localhost:7217/api/trading/signals -k

# 6. 停止模擬
curl -X POST https://localhost:7217/api/trading/stop -k
```

---

## 11. Fugle 即時行情（進階）

### 11.1 什麼是 Fugle？

[Fugle](https://developer.fugle.tw/) 是台灣的股市即時行情 API 服務，提供 WebSocket Streaming。

> ⚠️ **注意：** Fugle WebSocket Streaming 需要**付費 API 方案**。免費方案僅支援 5 個訂閱通道。

### 11.2 啟用步驟

1. **取得 API Token：** 前往 [Fugle 開發者平台](https://developer.fugle.tw/) 申請
2. **填入 Token：** 修改 `appsettings.json` 的 `Fugle:ApiToken`
3. **取消程式碼註解：** 在 `Program.cs` 中取消以下行的註解：
   ```csharp
   builder.Services.AddFugleMarketDataFeed(builder.Configuration);
   ```
4. **重新建置並啟動**

### 11.3 支援行情頻道

| 頻道 | 資料型態 | 系統輸出 |
|------|----------|----------|
| `trades` | 即時逐筆成交 | → TickData |
| `candles` | 分鐘 K 線 | → BarData |
| `books` | 最佳五檔委託 | → TickData（中間價）|
| `aggregates` | 聚合報價 | → TickData |
| `indices` | 指數 | 僅 Log |

### 11.4 WebSocket 參數

| 參數 | 預設值 | 說明 |
|------|--------|------|
| PingIntervalSeconds | 30 | 心跳間隔 |
| MaxReconnectAttempts | 0 | 最大重連次數（0 = 無限）|
| ReconnectBaseDelayMs | 1,000 | 重連基礎延遲 |
| ReconnectMaxDelayMs | 30,000 | 重連最大延遲（指數退避）|
| TickChannelCapacity | 10,000 | Tick 緩衝區上限 |
| BarChannelCapacity | 1,000 | Bar 緩衝區上限 |

### 11.5 Free Tier 限制

免費方案限制：
- 最多同時 **5 個** WebSocket 訂閱
- 系統會自動拒絕超出的訂閱請求並記錄警告日誌

---

## 12. 系統架構與擴展

### 12.1 分層依賴

```
QuantTrading.Web  ──▶  QuantTrading.Infrastructure  ──▶  QuantTrading.Core
  (Blazor + API)           (Fugle + JSON Store)            (策略 + 風控)
     展示層                    基礎設施層                     核心層
```

- **Core 層零依賴：** 不引用任何 NuGet 套件
- **全部 Singleton：** Blazor Server Circuit 需要共享狀態
- **Factory 模式：** `TradingEngineFactory.Create()` 每次建立全新引擎，防止跨次模擬狀態殘留

### 12.2 擴展方式

#### 新增策略

1. 在 `StrategyType` enum 加入新類型
2. 在 `StrategyEngine` 加入新的 `Evaluate` 方法
3. 在 `ProcessTickAsync` / `ProcessBarAsync` 加入路由
4. 加入對應的 Config record
5. 撰寫測試

#### 新增行情源

1. 實作 `IMarketDataFeed` 介面
2. 在 Infrastructure 層建立新的 Feed 類別
3. 建立 DI 擴充方法
4. 在 `Program.cs` 註冊

#### 替換持久化

1. 實作 `IConfigurationStore` 或 `ITradeJournalStore`
2. 建立新的 Store（如 SQLite、PostgreSQL）
3. 在 `Program.cs` 替換 DI 註冊

### 12.3 持久化檔案位置

| 檔案 | 路徑 | 用途 |
|------|------|------|
| 交易組態 | `src/QuantTrading.Web/Data/trading-config.json` | 策略 & 風控參數 |
| 交易日誌 | `src/QuantTrading.Web/Data/trade-journal.json` | 手動交易紀錄 |
| 設定範本 | `src/QuantTrading.Web/appsettings.template.json` | 設定範本（已入版控）|
| 實際設定 | `src/QuantTrading.Web/appsettings.json` | 執行時讀取（不入版控）|

---

## 13. 常見問題 FAQ

### 🔧 安裝 & 啟動

**Q: 雙擊 `run-web.bat` 沒有反應**  
A: 確認已安裝 .NET 8 SDK。執行 `dotnet --version` 確認版本。

**Q: 啟動時報 「API Token is not configured」**  
A: 確認 `appsettings.json` 存在且 `Fugle:ApiToken` 非空。只跑模擬填 `"demo"` 即可。

**Q: HTTPS 顯示憑證不信任**  
A: 執行 `dotnet dev-certs https --trust`，或改用 `http://localhost:5148`。

**Q: 框架錯誤 「net8.0 was not found」**  
A: 請安裝 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0

### 📊 模擬相關

**Q: 點擊 Start 後沒有訊號產生？**  
A:  
1. 確認 Watchlist 中至少有一檔股票
2. 模擬需要時間完成（取決於 Tick Delay）
3. 不是每次都會觸發訊號，要看合成行情是否達到策略條件
4. 可降低 Gap Strength% 或 Dip Threshold% 讓條件更容易碰觸

**Q: 模擬卡住或無回應？**  
A: 點擊 Stop → Clear → 重試。

**Q: 為什麼訊號被拒絕了？**  
A: 查看 Signal Log 的 Rejected Tab：
- `RejectMaxTrades`：今日已達 5 筆上限
- `RejectDailyLoss`：累計虧損已達上限
- `RejectRisk`：停損價格異常

### 📓 交易日誌

**Q: 交易紀錄儲存在哪裡？**  
A: `src/QuantTrading.Web/Data/trade-journal.json`

**Q: 重啟後交易紀錄會消失嗎？**  
A: 不會，所有紀錄持久化在 JSON 檔案中。

**Q: 可以手動編輯 JSON 嗎？**  
A: 可以，但請在系統停止時編輯。系統能容忍損壞的 JSON（回傳空集合）。

### ⚙️ 設定參數

**Q: 修改參數後何時生效？**  
A: 儲存後，啟動新的模擬時生效。正在進行的模擬不受影響。

**Q: 如何恢復預設值？**  
A: 在 Configuration 頁面點擊「Reset Defaults」按鈕。

### 🔌 Fugle 即時行情

**Q: 連線顯示 「Forbidden resource」**  
A: 免費方案不支援 WebSocket。需升級付費方案。未升級前保持 `AddFugleMarketDataFeed` 為註解狀態。

**Q: WebSocket 斷線了？**  
A: 系統有 Polly 自動重連機制（Exponential Backoff），預設無限重試。

---

## 14. 附錄

### 14.1 批次檔一覽

| 檔案 | 功能 | 說明 |
|------|------|------|
| `run-web.bat` | 啟動 Dashboard | 建置 + 啟動 Web 伺服器 |
| `run-tests.bat` | 執行測試 | 單元 + E2E 測試 |
| `build.bat` | 完整建置 | Restore → Build (Release) → Test |

### 14.2 命令列速查

```powershell
# 建置
dotnet build

# 啟動
dotnet run --project src/QuantTrading.Web

# 全部測試
dotnet test

# 單元測試
dotnet test tests/QuantTrading.Core.Tests

# E2E 測試
dotnet test tests/QuantTrading.E2E.Tests

# 清除
dotnet clean
```

### 14.3 測試分佈

| 測試類別 | 數量 | 涵蓋範圍 |
|----------|------|----------|
| Strategy A 單元測試 | 7 | 盤前試搓策略 |
| Strategy B 單元測試 | 8 | 盤中低接策略 |
| RiskManager 測試 | 10 | 風控管理器 |
| 行情模擬器測試 | 4 | 合成行情 |
| Fugle WebSocket 測試 | 38 | WebSocket 整合 |
| 交易日誌測試 | 27 | 持久化 + CSV 匯出 |
| REST API E2E | 8 | 端對端 API |
| Pipeline E2E | 5 | 完整行情→訊號流程 |
| 日誌 CRUD E2E | 9 | 交易日誌 E2E |

**合計：116 個測試，全部通過。**

---

> 📚 更多技術細節請參考 [ARCHITECTURE.md](ARCHITECTURE.md)。  
> 📋 策略規格書請參考 [STRATEGY_SPEC.md](STRATEGY_SPEC.md)。
