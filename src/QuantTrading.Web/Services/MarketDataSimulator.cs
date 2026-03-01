using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Web.Services;

/// <summary>
/// 模擬行情資料產生器 — 產生試搓 Tick 與盤中 Tick/Bar 資料。
/// 用於 UI 模擬測試，不連接真實行情源。
/// </summary>
public class MarketDataSimulator
{
    private readonly Random _rng;

    /// <summary>建立模擬器（隨機種子）。</summary>
    public MarketDataSimulator() { _rng = new Random(); }

    /// <summary>建立模擬器（固定種子，供測試用）。</summary>
    public MarketDataSimulator(int seed) { _rng = new Random(seed); }

    /// <summary>
    /// 產生 Strategy A 試搓情境 (08:30 ~ 09:00)
    /// </summary>
    public List<TickData> GeneratePreMarketTicks(
        string ticker,
        decimal refPrice,
        DateTime baseDate,
        bool strongGap = true,
        bool fakeout = false)
    {
        var ticks = new List<TickData>();
        var startTime = baseDate.Date.Add(new TimeSpan(8, 30, 0));
        var endTime = baseDate.Date.Add(new TimeSpan(9, 0, 0));

        decimal price = strongGap
            ? refPrice * 1.015m  // 漲 1.5%，符合 > 1% 門檻
            : refPrice * 1.005m; // 漲 0.5%，不符合門檻

        // 每 10 秒一筆試搓 tick
        for (var t = startTime; t <= endTime; t = t.AddSeconds(10))
        {
            decimal noise = (decimal)(_rng.NextDouble() * 0.002 - 0.001) * refPrice;
            decimal tickPrice = price + noise;

            // 模擬 fakeout：在 08:55 之後大幅回檔
            if (fakeout && t.TimeOfDay >= new TimeSpan(8, 55, 0))
            {
                tickPrice = refPrice * 0.998m; // 跌回參考價以下
            }

            ticks.Add(new TickData(ticker, Math.Round(tickPrice, 2), 100, t));
        }

        return ticks;
    }

    /// <summary>
    /// 產生 Strategy B 盤中情境 (含 VWAP、急跌、爆量)
    /// </summary>
    public (List<BarData> Bars, List<TickData> Ticks) GenerateIntradayData(
        string ticker,
        decimal openPrice,
        DateTime baseDate,
        bool triggerDipSignal = true)
    {
        var bars = new List<BarData>();
        var ticks = new List<TickData>();
        var barStart = baseDate.Date.Add(new TimeSpan(9, 1, 0));

        decimal price = openPrice;
        long baseVolume = 500;

        // 產生 20 根 1-Min K 棒
        for (int i = 0; i < 20; i++)
        {
            var barTime = barStart.AddMinutes(i);
            decimal change = (decimal)(_rng.NextDouble() * 0.006 - 0.003);

            // 模擬急跌：在第 10~12 根大幅下跌
            if (triggerDipSignal && i >= 10 && i <= 12)
            {
                change = -0.008m; // 急跌
            }

            decimal open = price; // Bar 開盤 = 前一根收盤
            price = Math.Round(price * (1 + change), 2); // 新收盤價
            decimal high = Math.Round(Math.Max(open, price) * 1.001m, 2);
            decimal low = Math.Round(Math.Min(open, price) * 0.999m, 2);

            // 爆量：在急跌後 (第 12 根) 放量
            long volume = baseVolume + (long)(_rng.NextDouble() * 200);
            if (triggerDipSignal && i == 12)
            {
                volume = baseVolume * 5; // 5 倍量 > 平均量 × 3
            }

            bars.Add(new BarData(ticker, open, high, low, price, volume, barTime));
        }

        // 在爆量 K 棒之後產生反彈 tick 序列
        if (triggerDipSignal)
        {
            var dipTime = barStart.AddMinutes(12).AddSeconds(30);
            decimal dipPrice = price * 0.975m; // 已跌到低點

            // 先一筆下跌 tick (滿足 dip 條件)
            ticks.Add(new TickData(ticker, Math.Round(dipPrice, 2), 200, dipTime));

            // 再一筆上漲 tick (止跌確認)
            ticks.Add(new TickData(ticker, Math.Round(dipPrice * 1.002m, 2), 150, dipTime.AddSeconds(1)));
        }

        return (bars, ticks);
    }

    /// <summary>
    /// 產生完整交易日模擬資料 (Strategy A + B)
    /// </summary>
    public SimulationScenario GenerateFullDayScenario(
        string ticker,
        decimal refPrice,
        DateTime baseDate)
    {
        var preMarketTicks = GeneratePreMarketTicks(ticker, refPrice, baseDate, strongGap: true);
        var (bars, intradayTicks) = GenerateIntradayData(ticker, refPrice * 1.015m, baseDate, triggerDipSignal: true);

        return new SimulationScenario(ticker, refPrice, baseDate, preMarketTicks, bars, intradayTicks);
    }
}

public record SimulationScenario(
    string Ticker,
    decimal RefPrice,
    DateTime Date,
    List<TickData> PreMarketTicks,
    List<BarData> IntradayBars,
    List<TickData> IntradayTicks
);
