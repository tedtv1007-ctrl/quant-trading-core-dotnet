# QuantTrading Core — 使用情境操作手冊

> **版本:** v3.0 | **更新日期:** 2026-03-31  
> 本手冊以實際交易情境為主軸，說明系統操作步驟。

---

## 目錄

1. [情境一：首次啟動系統](#情境一首次啟動系統)
2. [情境二：盤前設定與模擬 (日常開盤準備)](#情境二盤前設定與模擬)
3. [情境三：盤中即時監控](#情境三盤中即時監控)
4. [情境四：盤後記錄與績效回顧](#情境四盤後記錄與績效回顧)
5. [情境五：週末策略回測與參數調整](#情境五週末策略回測與參數調整)
6. [情境六：風控觸發與危機處理](#情境六風控觸發與危機處理)
7. [情境七：Docker 部署到伺服器](#情境七docker-部署到伺服器)
8. [情境八：API 整合與自動化](#情境八api-整合與自動化)

---

## 情境一：首次啟動系統

### 使用者背景
你剛 clone 專案，想要啟動系統看看功能。

### 操作步驟

#### Step 1: 啟動 Web 應用

```bash
cd quant-trading-core-dotnet
dotnet run --project src/QuantTrading.Web
```

#### Step 2: 開啟瀏覽器

前往 `https://localhost:5001` 或 `http://localhost:5000`

#### Step 3: 確認系統正常

你會看到 Dashboard 首頁，顯示：

```
┌─────────────────────────────────────────────────┐
│ ⚡ QuantTrading Trading Terminal                 │
├─────────────────────────────────────────────────┤
│                                                  │
│  [Status: Idle]  [Watchlist: 0]  [Trades: 0/5]  │
│  [Daily Loss: $0]  [Signals: 0]                  │
│                                                  │
│  ── Monitored Consoles ──                        │
│  (Watchlist is empty - add stocks above)         │
│                                                  │
│  ── Global Live Ticks ──                         │
│  No tick data                                    │
│                                                  │
└─────────────────────────────────────────────────┘
```

#### Step 4: 確認 Health Check

瀏覽器開啟 `http://localhost:5000/api/health`，應顯示 `Healthy`。

---

## 情境二：盤前設定與模擬

### 使用者背景
今天是交易日，早上 8:00 你想在開盤前設定監控清單並執行模擬測試。

### 操作步驟

#### Step 1: 設定策略參數

前往 **Configuration** 頁面 (`/configuration`)

```
┌─────────────────────────────────────────────────┐
│ ⚙ Strategy & Risk Configuration                 │
├──────────┬──────────┬──────────────────────────┤
│ Strategy A│ Strategy B│ Risk Management          │
│ Gap: 1%   │ Dip: 2%  │ Risk/Trade: $1,100      │
│ SL: 1%    │ Vol: 2x  │ Max Loss: $5,000        │
│ Fakeout:  │ SL: 1%   │ Max Trades: 5           │
│ 0.5%      │          │                          │
├──────────┴──────────┴──────────────────────────┤
│ Timeline:                                        │
│ [Strategy A: 08:30→08:59] [⚡09:00] [B: 09:01→13:25]│
└─────────────────────────────────────────────────┘
```

- 確認 Gap Strength 設定為 **1%**（適合大盤震盪日可調高）
- 確認 Risk Per Trade 為 **$1,100**（可依帳戶大小調整）
- 點擊 **Save All** 儲存

#### Step 2: 新增監控股票

前往 **Simulation** 頁面 (`/simulation`)

操作方式 A — 手動輸入：
1. 在 Ticker 欄位輸入 `2330`
2. Ref Price 輸入 `600`（昨日收盤價）
3. 點擊 **Add**

操作方式 B — 快速新增：
- 點擊 `2330 台積電` 按鈕快速加入

```
┌─────────────────────────────────────────────────┐
│ Watchlist                          [2 stocks]    │
├──────────┬──────────┬────────┬──────────────────┤
│ Ticker   │ Ref Price│ Status │                   │
│ 2330     │ $600.0   │ Idle   │ 🗑                │
│ 2317     │ $100.0   │ Idle   │ 🗑                │
└──────────┴──────────┴────────┴──────────────────┘
```

#### Step 3: 執行模擬

1. 設定 **Simulation Date** 為今天
2. 調整 **Tick Delay** 為 50ms（快速模擬）
3. 點擊 **Start All (2)**

```
┌─────────────────────────────────────────────────┐
│ Live Status                    [Running 2 stocks]│
├────────┬────────┬────────┬──────────────────────┤
│Stocks:2│Trades:1│Signals:│Rejected:0            │
│        │        │   1    │                       │
├────────┴────────┴────────┴──────────────────────┤
│ Per-Ticker Latest Price                          │
│ ┌──────────┐  ┌──────────┐                      │
│ │   2330   │  │   2317   │                      │
│ │  609.00  │  │  101.50  │                      │
│ └──────────┘  └──────────┘                      │
├─────────────────────────────────────────────────┤
│ Recent Signals                                   │
│ 08:59:55 OpenGap 2330 Entry:609 SL:602 Size:157 │
└─────────────────────────────────────────────────┘
```

#### Step 4: 查看模擬結果

模擬完成後：
- 前往 **Signal Log** 查看所有產出的訊號
- 前往 **Event Log** 查看決策過程的完整日誌

---

## 情境三：盤中即時監控

### 使用者背景
開盤後，你在 Dashboard 監控多支股票的即時狀態。

### 操作步驟

#### Step 1: Dashboard 即時監控

回到 **Dashboard** 首頁 (`/`)

每支股票的面板會即時更新：

```
┌───────────────────────────────────────────────────────────────┐
│ 2330                               [Pre-Market...]           │
│                                                               │
│    ╱╲                                                        │
│   ╱  ╲╱╲                      609.50                         │
│  ╱      ╲                     ▲ +1.58%                       │
│                                                               │
│ LONG 65% ████████░░░░ SHORT 35%                              │
│                                                               │
│ ── Recent Signals ──            ── Risk Rejections ──        │
│ 08:59 OpenGap  609.0 SL:602.0  │                            │
│                    157 股       │                            │
└───────────────────────────────────────────────────────────────┘
```

#### Step 2: 查看訊號詳情

當訊號產出時，Signal 面板會顯示：
- **時間**: 訊號觸發時間
- **策略**: OpenGap / IntradayDip
- **進場價**: 建議買入價格
- **停損價**: 停損設定
- **部位大小**: 建議買入股數

#### Step 3: 監控風控狀態

如果出現拒絕，右側 Rejections 面板會顯示原因：
- `RejectRisk` — 策略條件不符 (如假突破)
- `RejectMaxTrades` — 當日交易筆數已滿
- `RejectDailyLoss` — 虧損已達上限

#### Step 4: Event Log 追蹤決策過程

前往 **Event Log** (`/events`)，可以看到完整的系統決策脈絡：

```
┌──────────────┬─────────┬──────────┬────────────────────────┐
│ Time         │ Level   │ Category │ Message                │
├──────────────┼─────────┼──────────┼────────────────────────┤
│ 08:30:00.123 │ Info    │ System   │ Simulation started     │
│ 08:55:10.456 │ Info    │ Signal   │ OpenGap MarketBuy 2330 │
│ 08:59:55.789 │ Warning │ Risk     │ Signal rejected: 2317  │
│ 09:07:30.012 │ Info    │ Signal   │ IntradayDip LimitBuy   │
└──────────────┴─────────┴──────────┴────────────────────────┘
```

---

## 情境四：盤後記錄與績效回顧

### 使用者背景
收盤後 13:30，你要記錄今天的實際成交，並回顧當月績效。

### 操作步驟

#### Step 1: 記錄交易

前往 **Trade Journal** (`/trades`)

填寫表單：

```
┌─────────────────────────────┐
│ ✚ New Trade                 │
├─────────────────────────────┤
│ Trade Date:  [2026-03-31]   │
│ Ticker:      [2330       ]  │
│ Direction:   [Buy     ▾]    │
│ Price:     $ [609.00     ]  │
│ Quantity:    [1000   ] 股   │
│ Strategy:    [Strategy A ▾] │
│ Note:        [盤前跳空買進] │
│                             │
│ [✓ Add Trade]               │
└─────────────────────────────┘
```

記錄賣出交易：

```
│ Ticker:      [2330       ]  │
│ Direction:   [Sell    ▾]    │
│ Price:     $ [615.00     ]  │
│ Quantity:    [1000   ] 股   │
│ Strategy:    [Strategy A ▾] │
│ Note:        [達停利點賣出] │
```

#### Step 2: 查看每日摘要

左側自動顯示：

```
┌────────────────────┐
│ Day Summary        │
├────────────────────┤
│ Trades:   4        │
│ Buy Total: $709K   │
│ Sell Total: $735K  │
│ Net:      +$26K ▲  │
└────────────────────┘
```

#### Step 3: 匯出 CSV

點擊右上 **CSV** 按鈕，下載 `trade-journal-20260331-xxxxxx.csv`

#### Step 4: 績效分析

前往 **Analytics** (`/analytics`)

```
┌──────────────────────────────────────────────────────┐
│ 📈 Performance Analytics                              │
├──────────┬──────────┬──────────┬──────────┬──────────┤
│Total P/L │ Win Rate │ Trades   │Profit Fac│Max DD    │
│+$126,000 │  68.5%   │   45     │   2.35   │-$12,000  │
│  ▲ 綠色  │          │ 23 pairs │          │  ▼ 紅色  │
├──────────┴──────────┴──────────┴──────────┴──────────┤
│                                                       │
│ Win/Loss Detail    │ Strategy     │ Daily P/L          │
│ Avg Win: +$8,200   │ StratA: 60%  │ 03/31 ████ +26K   │
│ Avg Loss: -$3,500  │ StratB: 30%  │ 03/30 ██ -5K      │
│ Buy: $2.1M         │ Manual: 10%  │ 03/29 █████ +35K  │
│ Sell: $2.2M        │              │ 03/28 ███ +12K    │
│ Net: +$126K        │              │ ...               │
│                    │              │                    │
└──────────────────────────────────────────────────────┘
```

---

## 情境五：週末策略回測與參數調整

### 使用者背景
週末時間，你想回測上週的策略表現，並調整參數準備下週交易。

### 操作步驟

#### Step 1: 分析上週績效

前往 **Analytics**，點擊 **This Week** 查看本週績效：
- 檢查 Win Rate 是否達標 (目標 > 60%)
- 檢查 Profit Factor (目標 > 1.5)
- 檢查 Max Drawdown 是否在容許範圍

#### Step 2: 調整策略參數

前往 **Configuration**：

如果上週 Strategy A 的假突破率過高：
- 提高 **Gap Strength %** 從 1% → 1.5%（更嚴格的跳空門檻）
- 提高 **Fakeout Pullback %** 從 0.5% → 0.8%（更敏感的回檔偵測）

如果 Strategy B 觸發過少：
- 降低 **Dip Threshold %** 從 2% → 1.5%（更寬鬆的低接條件）
- 降低 **Volume Spike ×** 從 2.0 → 1.5（降低爆量要求）

點擊 **Save All** 儲存。

#### Step 3: 模擬驗證新參數

前往 **Simulation**：
1. 加入上週監控的股票清單
2. 執行模擬，觀察新參數下的訊號產出
3. 比對 Signal Log 確認策略行為符合預期

#### Step 4: 查看 Event Log 的決策脈絡

前往 **Event Log**，過濾 Category = `Risk`，檢查：
- 有多少訊號被拒絕？原因是什麼？
- 是否有不必要的拒絕？

---

## 情境六：風控觸發與危機處理

### 使用者背景
盤中某檔股票急跌，觸發流動性風險，系統自動啟動緊急機制。

### 操作步驟

#### Step 1: 流動性風險自動觸發

當股價距離跌停 ≤ 2% 時，系統自動：
1. **暫停該標的的所有新進場訊號**
2. 如果有持多單位，**自動產出 MarketSell 緊急訊號**

Dashboard 上會看到：

```
│ 2330               [Emergency: Liquidity Risk]   │
│                                                   │
│  553.00  ▼ -7.8%                                 │
│                                                   │
│ ── Risk Rejections ──                             │
│ 10:15 RejectRisk (Liquidity Suspended)           │
```

#### Step 2: Event Log 追蹤

前往 **Event Log**，過濾 Level = `Warning`：

```
│ 10:15:01 │ Warning │ Risk │ Signal rejected: OpenGap 2330 — RejectRisk │
│ 10:15:02 │ Warning │ Risk │ Emergency Sell triggered: 2330              │
```

#### Step 3: 每日虧損上限觸發

當累計虧損達到 $5,000 TWD 上限：
- Dashboard 的 Daily Loss 卡片轉為紅色
- 所有後續訊號都會被拒絕 (reason: `RejectDailyLoss`)
- Event Log 記錄完整的拒絕事件鏈

#### Step 4: 次交易日自動重置

每日開始時呼叫 `RiskManager.ResetDaily()`：
- 交易計數歸零
- 累計虧損歸零
- Event Log 記錄 "Daily reset completed"

---

## 情境七：Docker 部署到伺服器

### 使用者背景
你想把系統部署到遠端伺服器（Linux VPS），方便遠端監控。

### 操作步驟

#### Step 1: 複製專案到伺服器

```bash
git clone https://github.com/your-repo/quant-trading-core-dotnet.git
cd quant-trading-core-dotnet
```

#### Step 2: 啟動 Docker Compose

```bash
docker compose up -d
```

系統啟動後可在 `http://YOUR_IP:3000` 訪問。

#### Step 3: 驗證健康狀態

```bash
curl http://localhost:3000/api/health
# 回傳: Healthy
```

#### Step 4: 查看容器日誌

```bash
docker compose logs -f quant-trading-web
```

#### Step 5: 資料備份

交易紀錄存放在 Docker volume `trading-data` 中：

```bash
# 備份
docker compose cp quant-trading-web:/app/Data ./backup-data

# 還原
docker compose cp ./backup-data/. quant-trading-web:/app/Data
```

---

## 情境八：API 整合與自動化

### 使用者背景
你想用 Python/PowerShell 腳本自動取得訊號或績效數據。

### 操作步驟

#### Step 1: 查詢系統狀態

```bash
curl http://localhost:5000/api/trading/status
```

回傳範例：
```json
{
  "isSimulationRunning": false,
  "simulationStatus": "Idle",
  "dailyTradeCount": 0,
  "dailyRealizedLoss": 0,
  "watchlistCount": 0,
  "activeTickers": []
}
```

#### Step 2: 新增監控股票

```bash
curl -X POST http://localhost:5000/api/trading/watchlist \
  -H "Content-Type: application/json" \
  -d '{"ticker":"2330","refPrice":600}'
```

#### Step 3: 啟動模擬

```bash
curl -X POST http://localhost:5000/api/trading/simulate \
  -H "Content-Type: application/json" \
  -d '{"ticker":"2330","refPrice":600,"tickDelayMs":50}'
```

#### Step 4: 取得績效分析

```bash
curl "http://localhost:5000/api/trading/analytics?from=2026-03-01&to=2026-03-31"
```

回傳範例：
```json
{
  "totalTrades": 45,
  "completedPairs": 23,
  "winCount": 16,
  "lossCount": 7,
  "winRate": 69.6,
  "totalPnL": 126000,
  "averageWin": 8200,
  "averageLoss": -3500,
  "profitFactor": 2.34,
  "maxDrawdown": 12000,
  "dailyPnL": {"2026-03-31": 26000, "2026-03-30": -5000},
  "strategyBreakdown": {"Strategy A": 27, "Strategy B": 14, "Manual": 4}
}
```

#### Step 5: 取得系統事件

```bash
curl "http://localhost:5000/api/trading/events?count=20&level=Warning"
```

#### Step 6: PowerShell 自動化範例

```powershell
# 取得績效並判斷是否需要調整策略
$analytics = Invoke-RestMethod "http://localhost:5000/api/trading/analytics?from=2026-03-01"
if ($analytics.winRate -lt 50) {
    Write-Warning "Win rate below 50%! Consider adjusting strategy parameters."
}
if ($analytics.maxDrawdown -gt 20000) {
    Write-Warning "Max drawdown exceeds $20,000! Review risk settings."
}
Write-Host "Current P/L: $($analytics.totalPnL) TWD"
```

---

## 附錄：畫面說明對照表

| 頁面 | URL | 主要功能 |
|------|-----|----------|
| Dashboard | `/` | 即時監控、多空力量、走勢圖 |
| Simulation | `/simulation` | Watchlist 管理、模擬執行 |
| Signal Log | `/signals` | 訊號/拒絕/Tick/Bar 日誌 |
| Analytics | `/analytics` | P/L 分析、勝率、回撤 |
| Event Log | `/events` | 結構化系統事件 |
| Trade Journal | `/trades` | 交易記錄 CRUD |
| Configuration | `/configuration` | 策略/風控參數設定 |

---

## 附錄：系統架構圖 (NoFx-inspired)

```
                        QuantTrading Core
    ┌──────────────────────────────────────────────────────┐
    │              Blazor Server Dashboard                  │
    │     Dashboard · Simulation · Analytics · Events       │
    ├──────────────────────────────────────────────────────┤
    │                REST API (Minimal API)                 │
    │     /watchlist · /signals · /analytics · /events      │
    ├─────────┬──────────────┬──────────────┬──────────────┤
    │ Strategy │   Risk       │    Trade     │  System      │
    │ Engine   │   Manager    │  Statistics  │  Event Log   │
    │ (A + B)  │  + Liquidity │   Service    │   Service    │
    ├─────────┴──────────────┴──────────────┴──────────────┤
    │            Market Data Infrastructure                  │
    │    ┌────────────┐  ┌───────────┐  ┌──────────────┐   │
    │    │  Simulator  │  │  Fugle    │  │  Historical  │   │
    │    │ (模擬行情)  │  │ WebSocket │  │  Replay      │   │
    │    └────────────┘  └───────────┘  └──────────────┘   │
    ├──────────────────────────────────────────────────────┤
    │            Persistence (JSON File Store)               │
    │    trading-config.json  ·  trade-journal.json          │
    └──────────────────────────────────────────────────────┘
```
