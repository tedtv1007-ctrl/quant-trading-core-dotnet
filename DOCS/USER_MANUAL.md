# QuantTrading Core — 使用手冊

> **版本:** v2.0 | **最後更新:** 2026-03-01  
> **系統:** .NET 8 + Blazor Server | **測試:** 110 tests 全部通過

---

## 目錄

1. [快速開始](#1-快速開始)
2. [系統總覽](#2-系統總覽)
3. [Dashboard — 即時總覽](#3-dashboard--即時總覽)
4. [Simulation — 模擬操控台](#4-simulation--模擬操控台)
5. [Signal Log — 訊號日誌](#5-signal-log--訊號日誌)
6. [Trade Journal — 交易日誌](#6-trade-journal--交易日誌)
7. [Configuration — 參數設定](#7-configuration--參數設定)
8. [REST API 使用指南](#8-rest-api-使用指南)
9. [策略說明](#9-策略說明)
10. [風控規則](#10-風控規則)
11. [Fugle 即時行情（進階）](#11-fugle-即時行情進階)
12. [常見問題 FAQ](#12-常見問題-faq)
13. [附錄：鍵盤快捷操作](#13-附錄鍵盤快捷操作)

---

## 1. 快速開始

### 1.1 系統需求

| 項目 | 需求 |
|------|------|
| 作業系統 | Windows 10+ / macOS / Linux |
| 執行環境 | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 瀏覽器 | Chrome / Edge / Firefox（需支援 WebSocket）|
| IDE（選用）| Visual Studio 2022 / VS Code / Rider |

確認 .NET 8 已安裝：

```powershell
dotnet --version
# 應顯示 8.x.x
```

### 1.2 三步驟啟動

```powershell
# Step 1: 複製設定檔
cd src\QuantTrading.Web
copy appsettings.template.json appsettings.json

# Step 2: 還原套件
cd ..\..
dotnet restore

# Step 3: 啟動（三選一）
.\run-web.bat                                    # 方式一：批次檔
dotnet run --project src/QuantTrading.Web         # 方式二：命令列
# 方式三：VS 開啟 .sln → F5
```

### 1.3 開啟瀏覽器

啟動後訪問以下網址：

| 協定 | 網址 |
|------|------|
| HTTPS | **https://localhost:7217** |
| HTTP | http://localhost:5148 |

> **提示：** 首次使用 HTTPS 若顯示憑證警告，執行 `dotnet dev-certs https --trust` 或改用 HTTP。

---

## 2. 系統總覽

### 2.1 五大功能頁面

```
┌─────────────────────────────────────────────────┐
│  QuantTrading Dashboard                          │
│                                                   │
│  ┌─────┐  ┌────────────┬─────────────────────┐  │
│  │     │  │            │                       │  │
│  │  N  │  │  Dashboard │  即時狀態總覽         │  │
│  │  A  │  ├────────────┤                       │  │
│  │  V  │  │ Simulation │  模擬操控台           │  │
│  │     │  ├────────────┤                       │  │
│  │  M  │  │ Signal Log │  訊號/行情日誌        │  │
│  │  E  │  ├────────────┤                       │  │
│  │  N  │  │  Trade     │  手動交易紀錄         │  │
│  │  U  │  │  Journal   │                       │  │
│  │     │  ├────────────┤                       │  │
│  │     │  │ Config     │  策略參數調整         │  │
│  └─────┘  └────────────┴─────────────────────┘  │
└─────────────────────────────────────────────────┘
```

### 2.2 導航選單

| 區段 | 頁面 | 圖示 | 說明 |
|------|------|------|------|
| **OVERVIEW** | Dashboard | 📊 | 系統狀態、即時價格、最近訊號 |
| | Simulation | ▶️ | 加入股票、啟動/停止模擬 |
| **DATA** | Signal Log | ⚡ | 所有訊號、拒絕、Tick、Bar 紀錄 |
| **JOURNAL** | Trade Journal | 📓 | 手動記錄每日實際成交 |
| **SETTINGS** | Configuration | ⚙️ | 策略 A/B 參數、風控設定 |

---

## 3. Dashboard — 即時總覽

路徑：`/`（首頁）

### 3.1 全域狀態卡片

頁面頂部顯示 5 張狀態卡片：

| 卡片 | 說明 | 示例 |
|------|------|------|
| **Status** | 目前模擬狀態 | `IDLE` / `RUNNING` / `COMPLETED` |
| **Watchlist** | 監控清單股票數 | `3 stocks` |
| **Today's Trades** | 今日成交筆數 / 上限 | `2 / 5` |
| **Daily Loss** | 今日累計虧損 | `$1,100` |
| **Signals** | 總產出訊號數 | `5` |

### 3.2 每股即時卡片

對監控清單中每檔股票顯示：
- 股票代號 & 最新價格
- 漲跌幅百分比（綠漲紅跌）
- 該股的訊號數 / 被拒絕數

### 3.3 訊號 & 行情表格

| 表格 | 顯示筆數 | 欄位 |
|------|---------|------|
| **Recent Signals** | 最近 15 筆 | 時間、策略、買單類型、代號、進場價、停損價、部位、量比 |
| **Rejected Signals** | 最近 10 筆 | 時間、拒絕原因、代號 |
| **Recent Ticks** | 最近 30 筆 | 時間、代號、價格、量 |

> **自動更新：** Dashboard 每隔一段時間自動刷新，模擬運行中可即時看到新訊號。

---

## 4. Simulation — 模擬操控台

路徑：`/simulation`

### 4.1 操作流程

```
Step 1           Step 2              Step 3           Step 4
加入股票    →    設定參數       →    啟動模擬    →    觀察結果
                                     
[Add Stock]      Date: 2026/03/01    [▶ Start]       查看 Dashboard
or               Tick Delay: 50ms                    或 Signal Log
[Quick Add]
```

### 4.2 加入監控股票

**方式一：手動輸入**
1. 在「Ticker」欄位輸入股票代號（例如 `2330`）
2. 在「Ref Price」欄位輸入昨日收盤參考價（例如 `600`）
3. 點擊「+ ADD」按鈕

**方式二：快速新增**

點擊預設按鈕：

| 按鈕 | 代號 | 預設參考價 | 說明 |
|------|------|-----------|------|
| 2330 | 台積電 | 600 | 半導體龍頭 |
| 2317 | 鴻海 | 100 | 電子代工 |
| 2454 | 聯發科 | 950 | IC 設計 |
| 2881 | 富邦金 | 70 | 金融股 |

### 4.3 監控清單管理

- 每檔股票會顯示在清單表格中
- 可點擊 `🗑️` 按鈕移除
- 模擬運行中會顯示每檔股票的狀態

### 4.4 模擬參數

| 參數 | 說明 | 預設值 | 範圍 |
|------|------|--------|------|
| **Simulation Date** | 模擬的交易日日期 | 今天 | 任意日期 |
| **Tick Delay** | 每筆 Tick 之間的延遲 | 100ms | 10–500ms |

- ⬅ 滑桿左移（10ms）= 快速模擬，適合快速測試
- ➡ 滑桿右移（500ms）= 慢速播放，適合觀察每步細節

### 4.5 模擬控制

| 按鈕 | 功能 | 說明 |
|------|------|------|
| **▶ Start** | 啟動模擬 | 所有監控股票同步開始模擬完整交易日 |
| **⏹ Stop** | 停止模擬 | 立即中斷所有模擬 |
| **🗑 Clear** | 清除資料 | 清空所有訊號、Tick、Bar 資料 |

### 4.6 模擬執行過程

模擬分為兩個階段：

| 階段 | 時間 | 產出 | 策略 |
|------|------|------|------|
| **Phase 1: Pre-Market** | 08:30–09:00 | Tick 資料 | Strategy A 判定 |
| **Phase 2: Intraday** | 09:01–13:25 | Bar + Tick 資料 | Strategy B 判定 |

所有監控股票**同步平行**執行，共用同一個 RiskManager。

### 4.7 即時狀態面板

模擬運行中，右側面板即時顯示：
- 監控股票數、今日交易數、產出訊號數、被拒絕訊號數
- 每檔股票的最新價格
- 最近產出的訊號列表

---

## 5. Signal Log — 訊號日誌

路徑：`/signals`

### 5.1 篩選功能

- **Ticker Filter** 下拉選單：選擇特定股票代號，或「All Tickers」顯示全部

### 5.2 四個分頁 Tab

| Tab | 說明 | 關鍵欄位 |
|-----|------|----------|
| **Accepted Signals** | 風控通過的交易訊號 | 策略、買單類型、代號、進場價、停損價、部位大小、量比 |
| **Rejected** | 被風控拒絕的訊號 | 拒絕原因、策略、代號 |
| **Ticks** | 所有逐筆 Tick | 代號、價格、量、時間戳 |
| **Bars** | 所有 1 分鐘 K 棒 | 代號、OHLCV、時間戳 |

每個 Tab 標題都有數量 Badge 標示，方便快速判讀。

### 5.3 訊號欄位說明

| 欄位 | 說明 |
|------|------|
| **Strategy** | `OpenGap`（策略 A）或 `IntradayDip`（策略 B）|
| **Order Type** | `MarketBuy`（市價買）或 `LimitBuy`（限價買）|
| **Entry Price** | 建議進場價格 |
| **StopLoss** | 停損價格 |
| **Position Size** | 建議部位大小（股數）|
| **Volume Ratio** | 量比（Strategy B 專用，≥ 3.0 觸發）|

### 5.4 拒絕原因說明

| 原因 | 說明 |
|------|------|
| `RejectMaxTrades` | 今日交易已達上限（預設 5 筆）|
| `RejectDailyLoss` | 今日累計虧損已達上限 |
| `RejectRisk` | 一般風控規則拒絕 |

---

## 6. Trade Journal — 交易日誌

路徑：`/trades`

### 6.1 用途

手動記錄每日**實際成交**的交易紀錄，與模擬系統分開。

適用場景：
- 記錄你每天在券商實際下的單
- 追蹤買賣金額、損益
- 標記使用的策略
- 日後回顧交易操作

### 6.2 新增交易紀錄

左側「New Trade」表單：

| 欄位 | 說明 | 必填 | 範例 |
|------|------|------|------|
| **Date** | 交易日期 | ✅ | 2026-03-01 |
| **Ticker** | 股票代號 | ✅ | 2330 |
| **Direction** | 買 / 賣 | ✅ | Buy / Sell |
| **Price** | 成交價格 | ✅ (> 0) | 612.00 |
| **Quantity** | 成交股數 | ✅ (> 0) | 1000 |
| **Strategy** | 使用策略 | 選填 | Strategy A / Strategy B / 手動 / 其他 |
| **Note** | 備註 | 選填 | 盤前跳空買進 |

Strategy 下拉選項：
- `Strategy A` — 盤前試搓策略
- `Strategy B` — 盤中低接反彈
- `手動` — 手動判斷
- `其他` — 其他策略

填完後點擊「💾 Save Trade」儲存。

### 6.3 每日摘要

左側「📊 Day Summary」卡片自動統計：

| 項目 | 說明 |
|------|------|
| **Trades** | 所選日期的交易筆數 |
| **Buy Total** | 買進總金額 |
| **Sell Total** | 賣出總金額 |
| **Net** | 淨額 = 賣出 − 買進（正數為淨流入）|

### 6.4 交易歷史

右側「Trade History」表格顯示所有紀錄：

**日期篩選：**
- 選擇特定日期 → 只顯示該日紀錄
- 點擊「📋 All」按鈕 → 顯示所有日期

**表格欄位：**

| 欄位 | 說明 |
|------|------|
| Date | 交易日期 |
| Ticker | 股票代號 |
| Dir | 方向（🟢 Buy / 🔴 Sell）|
| Price | 成交價格 |
| Qty | 股數 |
| Amount | 金額 = Price × Qty |
| Strategy | 使用策略 |
| Note | 備註 |
| Actions | ✏️ 編輯 / 🗑️ 刪除 |

### 6.5 編輯 & 刪除

- **編輯：** 點擊 `✏️` 按鈕 → 左側表單載入該筆資料 → 修改後點擊「💾 Update Trade」
- **刪除：** 點擊 `🗑️` 按鈕 → 直接刪除該筆紀錄
- **取消編輯：** 點擊「❌ Cancel」回到新增模式

### 6.6 匯出 CSV

Trade History 標題列右側有一個 **「📥 Export CSV」** 按鈕：

1. 點擊按鈕
2. 系統根據目前篩選範圍匯出：
   - 若正在「All」模式 → 匯出全部紀錄
   - 若選擇了特定日期 → 只匯出該日紀錄
3. 瀏覽器自動下載 `trade-journal-YYYYMMDD-HHmmss.csv`

CSV 欄位：`Id, TradeDate, Ticker, Direction, Price, Quantity, Amount, Strategy, Note, CreatedAt`

> 💡 CSV 使用 UTF-8 BOM 編碼，Excel 可直接開啟中文不亂碼。

也可透過 REST API 直接取得：
```
GET /api/trading/journal/export?from=2026-01-01&to=2026-03-31
```

### 6.7 資料持久化

所有交易紀錄自動存入 `Data/trade-journal.json`，重啟系統後資料仍在。

---

## 7. Configuration — 參數設定

路徑：`/configuration`

### 7.1 概覽

所有策略和風控參數都可以在這個頁面即時修改並儲存。

### 7.2 Strategy A 參數（盤前試搓）

| 參數 | 說明 | 預設值 | 格式 |
|------|------|--------|------|
| **Monitor Start** | 開始監測時間 | 08:30:00 | HH:mm:ss |
| **Monitor End** | 判定截止時間 | 08:59:55 | HH:mm:ss |
| **Execution Time** | 下單執行時間 | 09:00:00 | HH:mm:ss |
| **Gap Strength %** | 跳空強度門檻 | 1.0% | 百分比 |
| **Fakeout Pullback %** | 假突破拉回門檻 | 0.5% | 百分比 |
| **Stop Loss Offset %** | 停損偏移 | 1.0% | 百分比 |

### 7.3 Strategy B 參數（盤中低接）

| 參數 | 說明 | 預設值 | 格式 |
|------|------|--------|------|
| **Active Start** | 策略啟動時間 | 09:01:00 | HH:mm:ss |
| **Active End** | 策略結束時間 | 13:25:00 | HH:mm:ss |
| **Dip Threshold %** | 低接門檻（低於 VWAP 幾 %）| 2.0% | 百分比 |
| **Volume Spike Multiplier** | 量能倍數門檻 | 3.0x | 倍數 |
| **Volume Lookback Bars** | 量能比較回顧 K 棒數 | 5 bar | 整數 |
| **Stop Loss Offset %** | 停損偏移 | 1.0% | 百分比 |

### 7.4 Risk Management 參數

| 參數 | 說明 | 預設值 |
|------|------|--------|
| **Risk Per Trade** | 單筆最大風險金額 | 1,100 TWD |
| **Max Daily Loss** | 每日最大累計虧損 | 5,000 TWD |
| **Max Daily Trades** | 每日最大交易筆數 | 5 |

### 7.5 部位計算公式

頁面會即時顯示根據 Risk Per Trade 推算的部位大小公式：

```
Position Size = Risk Per Trade / (Entry Price − Stop Loss Price)

範例：1,100 / (600 − 594) = 183 股
```

### 7.6 策略時間軸

頁面底部有一條視覺化時間軸，根據你填入的時間自動繪製：

```
08:30         08:59:55   09:00  09:01                           13:25
  ├── Strategy A 監測 ──┤ 下單 ├──── Strategy B 活躍 ──────────┤
```

### 7.7 儲存 & 重置

| 按鈕 | 功能 |
|------|------|
| **💾 Save All** | 將所有參數寫入 `Data/trading-config.json`，下次啟動自動載入 |
| **🔄 Reset Defaults** | 恢復所有參數為系統預設值 |

> **注意：** 儲存後，下次啟動模擬會自動使用新參數。目前正在執行的模擬不會受到影響。

---

## 8. REST API 使用指南

系統同時提供 REST API，可用於程式化操作或與外部系統整合。

基礎 URL：`https://localhost:7217/api/trading`

### 8.1 監控清單操作

#### 取得監控清單
```powershell
curl https://localhost:7217/api/trading/watchlist -k
```

回應範例：
```json
[
  { "ticker": "2330", "refPrice": 600, "status": "Ready", "isRunning": false },
  { "ticker": "2317", "refPrice": 100, "status": "Ready", "isRunning": false }
]
```

#### 新增股票
```powershell
curl -X POST https://localhost:7217/api/trading/watchlist `
  -H "Content-Type: application/json" `
  -d '{"ticker":"2330","refPrice":600}' -k
```

#### 移除股票
```powershell
curl -X DELETE https://localhost:7217/api/trading/watchlist/2330 -k
```

### 8.2 模擬操作

#### 啟動模擬
```powershell
curl -X POST https://localhost:7217/api/trading/simulate `
  -H "Content-Type: application/json" `
  -d '{"ticker":"2330","refPrice":600,"simulationDate":"2026-03-01","tickDelayMs":50}' -k
```

#### 停止模擬
```powershell
curl -X POST https://localhost:7217/api/trading/stop -k
```

### 8.3 查詢資料

#### 系統狀態
```powershell
curl https://localhost:7217/api/trading/status -k
```

回應範例：
```json
{
  "isSimulationRunning": true,
  "simulationStatus": "Running",
  "dailyTradeCount": 2,
  "dailyRealizedLoss": 0,
  "watchlistCount": 3,
  "activeTickers": ["2330", "2317", "2454"]
}
```

#### 訊號查詢
```powershell
# 所有訊號
curl https://localhost:7217/api/trading/signals -k

# 指定股票
curl "https://localhost:7217/api/trading/signals?ticker=2330" -k
```

#### 被拒絕訊號
```powershell
curl "https://localhost:7217/api/trading/rejections?ticker=2330" -k
```

#### Tick 資料
```powershell
curl "https://localhost:7217/api/trading/ticks?ticker=2330" -k
```

#### Bar 資料
```powershell
curl "https://localhost:7217/api/trading/bars?ticker=2330" -k
```

### 8.4 完整操作範例

```powershell
# 1. 加入多檔股票
curl -X POST https://localhost:7217/api/trading/watchlist `
  -H "Content-Type: application/json" -d '{"ticker":"2330","refPrice":600}' -k
curl -X POST https://localhost:7217/api/trading/watchlist `
  -H "Content-Type: application/json" -d '{"ticker":"2317","refPrice":100}' -k

# 2. 啟動快速模擬
curl -X POST https://localhost:7217/api/trading/simulate `
  -H "Content-Type: application/json" `
  -d '{"ticker":"2330","refPrice":600,"tickDelayMs":10}' -k

# 3. 等待幾秒
Start-Sleep -Seconds 5

# 4. 查看狀態
curl https://localhost:7217/api/trading/status -k

# 5. 查看訊號
curl https://localhost:7217/api/trading/signals -k

# 6. 停止
curl -X POST https://localhost:7217/api/trading/stop -k
```

---

## 9. 策略說明

### 9.1 Strategy A: 盤前試搓策略（Pre-Market Gap）

**目標：** 捕捉 09:00 開盤的強勢跳空訊號

**運作原理：**

```
08:30                                    08:59:55           09:00
  │                                         │                 │
  │   試搓 Tick 持續流入                    │                 │
  │   追蹤最高試搓價 (SimHigh)              │                 │
  │   偵測急跌假突破 (Fakeout)              │                 │
  │                                         │                 │
  │                                    判定時刻:              │
  │                                    ✅ 最新價 > 參考價×1.01│
  │                                    ✅ 無假突破偵測        │
  │                                         │           Market Buy!
  └─────────────────────────────────────────┘                 │
```

**判定條件：**
1. **跳空強度：** 試搓價 > 昨收 × (1 + GapStrength%)
2. **無假突破：** 監測期間無 > FakeoutPullback% 的急跌
3. **時間窗口：** 08:59:55 發出判定

**訊號類型：** `MarketBuy`（市價買進）

**適用場景：**
- 強勢個股盤前被大量掛買拉高
- 外資、投信大量買超的標的

### 9.2 Strategy B: 盤中低接反彈（Intraday Dip）

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

**判定條件（三重確認）：**
1. **量能放大：** 當前 Bar 量能 ≥ 最近 5 根 Bar 均量 × 3.0
2. **低接確認：** 價格 < VWAP × (1 − DipThreshold%)
3. **止跌反彈：** 下一筆 Tick 價格 > 上一筆 Tick 價格

**訊號類型：** `LimitBuy`（限價買進）

**適用場景：**
- 大盤急殺但個股基本面無虞
- 外資或法人調節持股時的超跌反彈

---

## 10. 風控規則

### 10.1 三道防線

```
     訊號產生
         │
    ┌────▼────┐
    │ 防線 1   │  今日交易 < 5 筆?
    │ 交易次數 │  ✅ → 通過
    └────┬────┘  ❌ → RejectMaxTrades
         │
    ┌────▼────┐
    │ 防線 2   │  累計虧損 < 5,000 TWD?
    │ 累計虧損 │  ✅ → 通過
    └────┬────┘  ❌ → RejectDailyLoss
         │
    ┌────▼────┐
    │ 防線 3   │  計算部位大小
    │ 部位計算 │  Position = 1,100 / (Entry − StopLoss)
    └────┬────┘
         │
    ✅ Accept → 產出 SignalContext
```

### 10.2 預設參數

| 規則 | 預設值 | 說明 |
|------|--------|------|
| **單筆風險** | 1,100 TWD | 每筆交易最大可能損失 |
| **每日上限** | 5 筆 | 每日最多執行 5 筆交易 |
| **累計虧損上限** | 5,000 TWD | 當日累計虧損達此值後停止交易 |

### 10.3 部位計算範例

| 股票 | 進場價 | 停損價 | 價差 | 部位大小 |
|------|--------|--------|------|----------|
| 2330 | 612 | 605.88 (1%) | 6.12 | 179 股 |
| 2317 | 103 | 101.97 (1%) | 1.03 | 1,068 股 |
| 2454 | 960 | 950.40 (1%) | 9.60 | 114 股 |

> **公式：** Position = 1,100 / (EntryPrice × StopLossOffset%)

---

## 11. Fugle 即時行情（進階）

### 11.1 什麼是 Fugle

[Fugle](https://developer.fugle.tw/) 是台灣的股市即時行情 API 服務，提供 WebSocket Streaming。

> **注意：** Fugle WebSocket Streaming 需要**付費 API 方案**。免費方案不支援。

### 11.2 啟用步驟

1. **申請 API Token：** 前往 Fugle 開發者平台取得付費 Token
2. **設定 Token：**
   ```json
   // src/QuantTrading.Web/appsettings.json
   {
     "Fugle": {
       "ApiToken": "YOUR_TOKEN_HERE"
     }
   }
   ```
3. **取消程式碼註解：**
   ```csharp
   // src/QuantTrading.Web/Program.cs
   // 取消這行的註解：
   builder.Services.AddFugleMarketDataFeed(builder.Configuration);
   ```
4. **重新建置並啟動**

### 11.3 Fugle 支援的行情頻道

| 頻道 | 資料類型 | 輸出 |
|------|----------|------|
| `trades` | 即時逐筆成交 | → TickData |
| `candles` | 分鐘 K 線 | → BarData |
| `books` | 最佳五檔委託 | → TickData（中間價）|
| `aggregates` | 聚合報價 | → TickData |
| `indices` | 指數 | 僅 Log |

### 11.4 WebSocket 韌性參數

| 參數 | 預設值 | 說明 |
|------|--------|------|
| PingIntervalSeconds | 30 | 心跳間隔（秒）|
| MaxReconnectAttempts | 0 | 最大重連次數（0 = 無限）|
| ReconnectBaseDelayMs | 1,000 | 重連基礎延遲 |
| ReconnectMaxDelayMs | 30,000 | 重連最大延遲（指數退避）|
| TickChannelCapacity | 10,000 | Tick 緩衝區上限 |
| BarChannelCapacity | 1,000 | Bar 緩衝區上限 |

---

## 12. 常見問題 FAQ

### 啟動問題

**Q: 雙擊 `run-web.bat` 沒有反應**  
A: 確認已安裝 .NET 8 SDK。打開 PowerShell 執行 `dotnet --version` 確認。

**Q: 啟動時報 "API Token is not configured"**  
A: 確認 `src/QuantTrading.Web/appsettings.json` 存在且 Fugle:ApiToken 非空。
   只跑模擬可填任意字串（如 `"demo"`）。

**Q: HTTPS 顯示憑證不信任**  
A: 執行 `dotnet dev-certs https --trust`，或改用 http://localhost:5148。

### 模擬問題

**Q: 點擊 Start 後沒有訊號產生**  
A: 
1. 確認已加入至少一檔股票到 Watchlist
2. 模擬需要一些時間完成（看 Tick Delay 設定）
3. 不是每次模擬都會觸發訊號，取決於合成行情是否達到策略條件

**Q: 模擬卡住或無回應**  
A: 點擊 Stop 按鈕停止模擬，然後 Clear 清除資料後重試。

**Q: 為什麼訊號被拒絕了？**  
A: 查看 Signal Log 的 Rejected Tab，常見原因：
- `RejectMaxTrades`：今日已達 5 筆上限
- `RejectDailyLoss`：累計虧損已達上限

### 交易日誌問題

**Q: 交易紀錄會儲存在哪裡？**  
A: 儲存在 `src/QuantTrading.Web/Data/trade-journal.json`，純文字 JSON 格式。

**Q: 重啟系統後交易紀錄會消失嗎？**  
A: 不會。所有紀錄都持久化在 JSON 檔案中。

**Q: 可以手動編輯 JSON 嗎？**  
A: 可以，但請在系統停止時編輯，避免格式錯誤。系統能容忍損壞的 JSON（回傳空集合）。

### 設定問題

**Q: 儲存設定後何時生效？**  
A: 下次啟動新的模擬時生效。正在執行的模擬不受影響。

**Q: 如何恢復預設值？**  
A: 在 Configuration 頁面點擊「Reset Defaults」按鈕。

### Fugle 問題

**Q: WebSocket 連線顯示 "Forbidden resource"**  
A: 免費方案不支援 WebSocket。需升級至付費方案。未升級前保持 `AddFugleMarketDataFeed` 在 Program.cs 中為註解狀態。

**Q: WebSocket 斷線了怎麼辦？**  
A: 系統有自動重連機制（Polly Exponential Backoff），預設無限重試。

---

## 13. 附錄：鍵盤快捷操作

### 批次檔

| 命令 | 說明 |
|------|------|
| `.\run-web.bat` | 啟動 Web Dashboard |
| `.\run-tests.bat` | 執行所有測試 |
| `.\build.bat` | 完整建置 + 測試（Release 模式）|

### 命令列

```powershell
# 建置
dotnet build

# 啟動
dotnet run --project src/QuantTrading.Web

# 執行所有測試
dotnet test

# 只跑單元測試
dotnet test tests/QuantTrading.Core.Tests

# 只跑 E2E 測試
dotnet test tests/QuantTrading.E2E.Tests

# 清除建置產物
dotnet clean
```

### 資料檔案位置

| 檔案 | 路徑 | 用途 |
|------|------|------|
| 交易組態 | `src/QuantTrading.Web/Data/trading-config.json` | 策略 & 風控參數 |
| 交易日誌 | `src/QuantTrading.Web/Data/trade-journal.json` | 手動交易紀錄 |
| 設定範本 | `src/QuantTrading.Web/appsettings.template.json` | Fugle 設定範本 |
| 實際設定 | `src/QuantTrading.Web/appsettings.json` | 執行時讀取（不入版控）|

---

> **回報問題或建議？** 請參考 [ARCHITECTURE.md](ARCHITECTURE.md) 了解系統架構，
> 或查閱 [STRATEGY_SPEC.md](STRATEGY_SPEC.md) 了解策略規格。
