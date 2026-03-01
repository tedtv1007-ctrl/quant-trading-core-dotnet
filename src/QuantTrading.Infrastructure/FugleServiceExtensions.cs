using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuantTrading.Core.Interfaces;
using QuantTrading.Infrastructure.Configuration;
using QuantTrading.Infrastructure.Fugle;

namespace QuantTrading.Infrastructure;

/// <summary>
/// DI 擴展方法 — 註冊 Fugle WebSocket 行情服務。
///
/// Usage:
///   builder.Services.AddFugleMarketDataFeed(builder.Configuration);
/// </summary>
public static class FugleServiceExtensions
{
    /// <summary>
    /// 註冊 FugleMarketDataFeed 為 IMarketDataFeed (Singleton)
    /// 並加入 BackgroundService 管理 WebSocket 生命週期。
    /// </summary>
    public static IServiceCollection AddFugleMarketDataFeed(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── 綁定 FugleOptions ────────────────────────────────────
        services.Configure<FugleOptions>(
            configuration.GetSection(FugleOptions.SectionName));

        // ── 註冊 FugleMarketDataFeed 為 Singleton ───────────────
        services.AddSingleton<FugleMarketDataFeed>();
        services.AddSingleton<IMarketDataFeed>(sp => sp.GetRequiredService<FugleMarketDataFeed>());

        // ── 註冊 BackgroundService (管理生命週期) ────────────────
        services.AddHostedService<FugleMarketDataHostedService>();

        return services;
    }
}
