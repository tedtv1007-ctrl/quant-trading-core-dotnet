using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Core;

/// <summary>
/// 預設工廠 — 每次建立全新的 RiskManager + StrategyEngine 實例。
/// </summary>
public class TradingEngineFactory : ITradingEngineFactory
{
    public (IStrategyEngine Engine, IRiskManager RiskManager) Create(
        RiskConfig? riskConfig = null,
        PreMarketGapConfig? gapConfig = null,
        IntradayDipConfig? dipConfig = null)
    {
        var riskManager = new RiskManager(riskConfig);
        var engine = new StrategyEngine(riskManager, gapConfig, dipConfig);
        return (engine, riskManager);
    }
}
