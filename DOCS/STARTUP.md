# QuantTrading Core — 啟動手冊

> **版本:** v1.0 | **最後更新:** 2026-03-01 | **框架:** .NET 8 + Blazor Server

---

## 目錄

1. [系統需求](#1-系統需求)
2. [專案結構](#2-專案結構)
3. [初次設定](#3-初次設定)
4. [啟動方式](#4-啟動方式)
5. [批次檔一覽](#5-批次檔一覽)
6. [REST API 參考](#6-rest-api-參考)
7. [Fugle 即時行情（選配）](#7-fugle-即時行情選配)
8. [常見問題](#8-常見問題)

---

## 1. 系統需求

| 項目 | 最低需求 |
|------|----------|
| OS | Windows 10+ / macOS / Linux |
| Runtime | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| IDE（選用）| Visual Studio 2022 / VS Code / Rider |
| 瀏覽器 | Chrome / Edge / Firefox（Blazor Server 需要 WebSocket 支援）|

驗證 SDK 已安裝：

```powershell
dotnet --version
# 應顯示 8.x.x
```

---

## 2. 專案結構

```
quant-trading-core-dotnet/
├── build.bat                          # 建置 + 測試
├── run-web.bat                        # 啟動 Web Dashboard
├── run-tests.bat                      # 只跑測試
├── quant-trading-core-dotnet.sln      # Solution 檔
│
├── src/
│   ├── QuantTrading.Core/             # 核心策略引擎、風控、Models
│   ├── QuantTrading.Infrastructure/   # Fugle WebSocket、DI 擴充
│   └── QuantTrading.Web/              # Blazor Server Dashboard + REST API
│       ├── appsettings.template.json  # ← 設定檔範本（已入版控）
│       ├── appsettings.json           # ← 實際設定檔（.gitignore 排除）
│       ├── Components/Pages/          # Blazor 頁面
│       └── Services/                  # API Endpoints、背景服務
│
├── tests/
│   ├── QuantTrading.Core.Tests/       # 單元測試 (89 tests)
│   └── QuantTrading.E2E.Tests/        # 端對端測試 (21 tests)
│
└── DOCS/
    ├── STARTUP.md                     # ← 本文件
    ├── STRATEGY_SPEC.md               # 策略規格書
    ├── ARCHITECTURE.md                # 系統架構說明文件
    └── USER_MANUAL.md                 # 使用手冊
```

---

## 3. 初次設定

### 3.1 複製設定檔

```powershell
cd src\QuantTrading.Web
copy appsettings.template.json appsettings.json
```

### 3.2 編輯 `appsettings.json`

```jsonc
{
  "Fugle": {
    "ApiToken": "<YOUR_FUGLE_API_TOKEN>",  // ← 填入你的 Fugle API Token
    // ...其餘保持預設即可
  }
}
```

> **注意：** `appsettings.json` 已被 `.gitignore` 排除，不會被提交到版控。
> 如果只跑模擬（不連 Fugle），可以填任意非空字串即可。

### 3.3 還原套件

```powershell
dotnet restore
```

---

## 4. 啟動方式

### 方式一：使用批次檔（最簡單）

```
雙擊 run-web.bat
```

瀏覽器開啟 **https://localhost:7217** 即可看到 Dashboard。

### 方式二：命令列

```powershell
# 從專案根目錄
dotnet run --project src/QuantTrading.Web
```

### 方式三：Visual Studio

1. 開啟 `quant-trading-core-dotnet.sln`
2. 將 **QuantTrading.Web** 設為啟動專案
3. 按 `F5` 或 `Ctrl+F5`

### 方式四：VS Code

1. 開啟專案資料夾
2. Terminal → `dotnet run --project src/QuantTrading.Web`
3. 或使用 C# Dev Kit 的 Run/Debug 功能

---

## 5. 批次檔一覽

| 檔案 | 功能 | 說明 |
|------|------|------|
| `build.bat` | 建置 + 全部測試 | Restore → Build (Release) → Test |
| `run-web.bat` | 啟動 Dashboard | 檢查設定檔 → Build → Run（https://localhost:7217）|
| `run-tests.bat` | 只跑測試 | Build (Debug) → Unit Tests → E2E Tests |

所有批次檔皆使用 `cd /d "%~dp0"` 定位到專案根目錄，可在任何路徑下雙擊執行。

---

## 6. REST API 參考

啟動後，所有 API 都在 `/api/trading` 路徑下。

### 監控清單

| Method | URL | 說明 |
|--------|-----|------|
| `GET` | `/api/trading/watchlist` | 取得監控清單 |
| `POST` | `/api/trading/watchlist` | 新增股票（Body: `{"ticker":"2330","refPrice":600}`）|
| `DELETE` | `/api/trading/watchlist/{ticker}` | 移除股票 |

### 模擬 & 行情

| Method | URL | 說明 |
|--------|-----|------|
| `POST` | `/api/trading/simulate` | 啟動模擬（Body: `{"ticker":"2330","refPrice":600,"tickDelayMs":100}`）|
| `POST` | `/api/trading/stop` | 停止模擬 |
| `GET` | `/api/trading/ticks?ticker=2330` | 最近 Tick 資料 |
| `GET` | `/api/trading/bars?ticker=2330` | 最近 Bar 資料 |

### 訊號 & 狀態

| Method | URL | 說明 |
|--------|-----|------|
| `GET` | `/api/trading/signals?ticker=2330` | 策略訊號 |
| `GET` | `/api/trading/rejections?ticker=2330` | 風控拒絕記錄 |
| `GET` | `/api/trading/status` | 系統狀態（模擬中/交易次數/損益）|

### 快速測試範例

```powershell
# 新增 2330 到監控清單 (參考價 600)
curl -X POST https://localhost:7217/api/trading/watchlist `
  -H "Content-Type: application/json" `
  -d '{"ticker":"2330","refPrice":600}' -k

# 啟動模擬
curl -X POST https://localhost:7217/api/trading/simulate `
  -H "Content-Type: application/json" `
  -d '{"ticker":"2330","refPrice":600,"tickDelayMs":50}' -k

# 查看訊號
curl https://localhost:7217/api/trading/signals -k

# 停止模擬
curl -X POST https://localhost:7217/api/trading/stop -k
```

---

## 7. Fugle 即時行情（選配）

### 啟用步驟

1. 取得 Fugle 付費 API Token（需 WebSocket Streaming 權限）
2. 將 Token 填入 `appsettings.json` 的 `Fugle:ApiToken`
3. 取消 `Program.cs` 中的註解：

```csharp
// 取消這行的註解：
builder.Services.AddFugleMarketDataFeed(builder.Configuration);
```

4. 重新建置並啟動

### 支援頻道

| 頻道 | 說明 | 輸出 |
|------|------|------|
| `trades` | 即時逐筆成交 | → `TickData` |
| `candles` | 分鐘 K 線 | → `BarData` |
| `books` | 最佳五檔 | → `TickData`（中間價）|
| `aggregates` | 聚合報價 | → `TickData` |
| `indices` | 指數 | 僅 Log |

### 重要參數

| 參數 | 預設值 | 說明 |
|------|--------|------|
| `PingIntervalSeconds` | 30 | 心跳間隔 |
| `MaxReconnectAttempts` | 0 | 0 = 無限重試 |
| `ReconnectBaseDelayMs` | 1000 | 重連基礎延遲 |
| `ReconnectMaxDelayMs` | 30000 | 重連最大延遲（exponential backoff）|
| `TickChannelCapacity` | 10000 | Tick 緩衝區大小（DropOldest）|
| `BarChannelCapacity` | 1000 | Bar 緩衝區大小 |

---

## 8. 常見問題

### Q: 啟動時報 "API Token is not configured"

A: 確認 `appsettings.json` 存在且 `Fugle:ApiToken` 非空。只跑模擬可填任意字串。

### Q: 建置失敗 "The framework 'net8.0' was not found"

A: 請安裝 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0

### Q: https://localhost:7217 顯示憑證不信任

A: 這是 Development 自簽憑證，執行以下命令信任：

```powershell
dotnet dev-certs https --trust
```

或使用 HTTP：http://localhost:5148

### Q: Fugle WebSocket 連線顯示 "Forbidden resource"

A: 免費方案不支援 WebSocket Streaming。需升級至 Fugle 付費方案。在升級前，請保持 `AddFugleMarketDataFeed` 處於註解狀態，使用內建模擬器即可。

### Q: 測試數量為何從 73 變成 80？

A: 系統持續新增測試。目前共 110 個測試（89 Unit + 21 E2E），涵蓋策略引擎、風控、Fugle WebSocket、交易日誌、REST API 等。

---

## 附錄：策略簡介

| 策略 | 名稱 | 進場條件 |
|------|------|----------|
| **A** | 開盤試搓（Pre-Market Gap）| 08:30–09:00 試搓價 > 參考價 × 1.01，且無驟降假突破 |
| **B** | 盤中低接反彈（Intraday Dip）| 價格 < VWAP − 門檻值，且量能 > 均量 × 3 |

風控規則：每日最多 5 筆 A 策略、單筆停損 1,100 TWD、全域停損限制。

詳見 [STRATEGY_SPEC.md](STRATEGY_SPEC.md)。
