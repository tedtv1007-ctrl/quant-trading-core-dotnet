using QuantTrading.Core;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;
using QuantTrading.Core.Services;
using QuantTrading.Infrastructure;
using QuantTrading.Infrastructure.Configuration;
using QuantTrading.Web.Components;
using QuantTrading.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Health Check ────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── 組態持久化 (JSON 檔案資料庫) ────────────────────────────────
var configStore = new JsonConfigurationStore(
    Path.Combine(builder.Environment.ContentRootPath, "Data", "trading-config.json"));
builder.Services.AddSingleton<IConfigurationStore>(configStore);

// 啟動時從檔案載入組態 (直接使用實例，不需 BuildServiceProvider)
TradingConfiguration tradingConfig;
try
{
    tradingConfig = configStore.LoadAsync().GetAwaiter().GetResult();
}
catch (Exception)
{
    // 若載入失敗 (例如環境權限問題)，回傳預設值以確保 App 能啟動
    tradingConfig = new TradingConfiguration();
}
builder.Services.AddSingleton(tradingConfig);

// 注意：個別 Config (GapConfig, DipConfig, RiskConfig) 應透過 TradingConfiguration 存取，
// 不另行註冊，避免 Singleton 快取到過時的值。

// ── 交易日誌持久化 (JSON 檔案資料庫) ────────────────────────────
builder.Services.AddSingleton<ITradeJournalStore>(
    new JsonTradeJournalStore(
        Path.Combine(builder.Environment.ContentRootPath, "Data", "trade-journal.json")));

// ── 注入交易系統服務 ─────────────────────────────────────────────
builder.Services.AddSingleton<TradingStateService>();
builder.Services.AddSingleton<SimulationBackgroundService>();
builder.Services.AddSingleton<ITradingEngineFactory, TradingEngineFactory>();

// ── 績效分析 & 系統事件日誌 (參考 NoFx Dashboard 設計) ──────────
builder.Services.AddSingleton<TradeStatisticsService>();
builder.Services.AddSingleton<SystemEventLogService>();

// ── Fugle 即時行情服務 (WebSocket) ──────────────────────────────
// 設定在 appsettings.json 的 "Fugle" 區段
// 需要 Fugle 付費方案 (WebSocket Streaming 權限) 才能啟用
builder.Services.AddFugleMarketDataFeed(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// ── Health Check endpoint ────────────────────────────────────────
app.MapHealthChecks("/api/health");

// ── REST API endpoints ───────────────────────────────────────────
app.MapTradingApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// 讓 WebApplicationFactory 可以找到 Program class
public partial class Program { }
