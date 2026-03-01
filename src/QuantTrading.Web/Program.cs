using QuantTrading.Core;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;
using QuantTrading.Infrastructure;
using QuantTrading.Infrastructure.Configuration;
using QuantTrading.Web.Components;
using QuantTrading.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── 組態持久化 (JSON 檔案資料庫) ────────────────────────────────
var configStore = new JsonConfigurationStore(
    Path.Combine(builder.Environment.ContentRootPath, "Data", "trading-config.json"));
builder.Services.AddSingleton<IConfigurationStore>(configStore);

// 啟動時從檔案載入組態 (直接使用實例，不需 BuildServiceProvider)
var tradingConfig = configStore.LoadAsync().GetAwaiter().GetResult();
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

// ── Fugle 即時行情服務 (WebSocket) ──────────────────────────────
// 設定在 appsettings.json 的 "Fugle" 區段
// 需要 Fugle 付費方案 (WebSocket Streaming 權限) 才能啟用
// builder.Services.AddFugleMarketDataFeed(builder.Configuration);

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

// ── REST API endpoints ───────────────────────────────────────────
app.MapTradingApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// 讓 WebApplicationFactory 可以找到 Program class
public partial class Program { }
