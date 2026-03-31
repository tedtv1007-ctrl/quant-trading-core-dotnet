using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Core.Services;

/// <summary>
/// 交易績效統計服務 — 從 TradeJournal 計算 P/L、勝率、最大回撤等指標。
/// 參考 NoFx 的 Dashboard 即時績效追蹤設計。
/// </summary>
public class TradeStatisticsService
{
    private readonly ITradeJournalStore _journalStore;

    public TradeStatisticsService(ITradeJournalStore journalStore)
    {
        _journalStore = journalStore;
    }

    /// <summary>計算指定期間的績效摘要</summary>
    public async Task<PerformanceSummary> GetPerformanceSummaryAsync(DateTime? from = null, DateTime? to = null)
    {
        var allTrades = await _journalStore.GetAllAsync();

        var filtered = allTrades.AsEnumerable();
        if (from.HasValue) filtered = filtered.Where(t => t.TradeDate >= from.Value.Date);
        if (to.HasValue) filtered = filtered.Where(t => t.TradeDate <= to.Value.Date);

        var trades = filtered.OrderBy(t => t.TradeDate).ThenBy(t => t.CreatedAt).ToList();

        if (trades.Count == 0)
            return PerformanceSummary.Empty;

        // 計算各標的的已實現損益 (配對法: FIFO)
        var pairResults = CalculatePairedPnL(trades);

        decimal totalPnL = pairResults.Sum(p => p.PnL);
        int totalPairs = pairResults.Count;
        int winCount = pairResults.Count(p => p.PnL > 0);
        int lossCount = pairResults.Count(p => p.PnL < 0);

        decimal winRate = totalPairs > 0 ? (decimal)winCount / totalPairs * 100 : 0;
        decimal avgWin = winCount > 0 ? pairResults.Where(p => p.PnL > 0).Average(p => p.PnL) : 0;
        decimal avgLoss = lossCount > 0 ? pairResults.Where(p => p.PnL < 0).Average(p => p.PnL) : 0;
        decimal profitFactor = avgLoss != 0 ? Math.Abs(avgWin / avgLoss) : 0;

        // 最大回撤
        decimal maxDrawdown = CalculateMaxDrawdown(pairResults);

        // 每日損益
        var dailyPnL = CalculateDailyPnL(trades);

        // 策略分布
        var strategyBreakdown = trades
            .GroupBy(t => t.Strategy ?? "Untagged")
            .ToDictionary(g => g.Key, g => g.Count());

        decimal buyTotal = trades.Where(t => t.Direction == TradeDirection.Buy).Sum(t => t.Amount);
        decimal sellTotal = trades.Where(t => t.Direction == TradeDirection.Sell).Sum(t => t.Amount);

        return new PerformanceSummary
        {
            TotalTrades = trades.Count,
            CompletedPairs = totalPairs,
            WinCount = winCount,
            LossCount = lossCount,
            WinRate = Math.Round(winRate, 1),
            TotalPnL = totalPnL,
            AverageWin = Math.Round(avgWin, 0),
            AverageLoss = Math.Round(avgLoss, 0),
            ProfitFactor = Math.Round(profitFactor, 2),
            MaxDrawdown = maxDrawdown,
            BuyTotal = buyTotal,
            SellTotal = sellTotal,
            DailyPnL = dailyPnL,
            StrategyBreakdown = strategyBreakdown
        };
    }

    /// <summary>FIFO 配對法計算已實現損益</summary>
    private List<PairResult> CalculatePairedPnL(List<TradeRecord> trades)
    {
        var results = new List<PairResult>();
        var openPositions = new Dictionary<string, Queue<(decimal Price, int Qty)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var trade in trades)
        {
            if (trade.Direction == TradeDirection.Buy)
            {
                if (!openPositions.ContainsKey(trade.Ticker))
                    openPositions[trade.Ticker] = new Queue<(decimal, int)>();
                openPositions[trade.Ticker].Enqueue((trade.Price, trade.Quantity));
            }
            else if (trade.Direction == TradeDirection.Sell)
            {
                if (!openPositions.TryGetValue(trade.Ticker, out var queue) || queue.Count == 0)
                    continue;

                int remainingSell = trade.Quantity;
                while (remainingSell > 0 && queue.Count > 0)
                {
                    var (buyPrice, buyQty) = queue.Peek();
                    int matchQty = Math.Min(remainingSell, buyQty);
                    decimal pnl = (trade.Price - buyPrice) * matchQty;

                    results.Add(new PairResult
                    {
                        Ticker = trade.Ticker,
                        BuyPrice = buyPrice,
                        SellPrice = trade.Price,
                        Quantity = matchQty,
                        PnL = pnl,
                        TradeDate = trade.TradeDate
                    });

                    remainingSell -= matchQty;
                    if (matchQty >= buyQty)
                        queue.Dequeue();
                    else
                        openPositions[trade.Ticker] = new Queue<(decimal, int)>(
                            new[] { (buyPrice, buyQty - matchQty) }.Concat(queue.Skip(1)));
                }
            }
        }

        return results;
    }

    /// <summary>計算最大回撤 (基於配對損益的累計曲線)</summary>
    private decimal CalculateMaxDrawdown(List<PairResult> pairs)
    {
        if (pairs.Count == 0) return 0;

        decimal cumPnL = 0;
        decimal peak = 0;
        decimal maxDrawdown = 0;

        foreach (var pair in pairs)
        {
            cumPnL += pair.PnL;
            if (cumPnL > peak) peak = cumPnL;
            decimal drawdown = peak - cumPnL;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }

    /// <summary>計算每日損益</summary>
    private Dictionary<DateTime, decimal> CalculateDailyPnL(List<TradeRecord> trades)
    {
        var dailyBuys = new Dictionary<DateTime, decimal>();
        var dailySells = new Dictionary<DateTime, decimal>();

        foreach (var t in trades)
        {
            var date = t.TradeDate.Date;
            if (t.Direction == TradeDirection.Buy)
            {
                dailyBuys.TryGetValue(date, out var existing);
                dailyBuys[date] = existing + t.Amount;
            }
            else
            {
                dailySells.TryGetValue(date, out var existing);
                dailySells[date] = existing + t.Amount;
            }
        }

        var allDates = dailyBuys.Keys.Union(dailySells.Keys).OrderBy(d => d);
        var result = new Dictionary<DateTime, decimal>();

        foreach (var date in allDates)
        {
            dailySells.TryGetValue(date, out var sell);
            dailyBuys.TryGetValue(date, out var buy);
            result[date] = sell - buy;
        }

        return result;
    }
}

/// <summary>配對結果</summary>
public class PairResult
{
    public string Ticker { get; set; } = "";
    public decimal BuyPrice { get; set; }
    public decimal SellPrice { get; set; }
    public int Quantity { get; set; }
    public decimal PnL { get; set; }
    public DateTime TradeDate { get; set; }
}

/// <summary>績效摘要</summary>
public class PerformanceSummary
{
    public int TotalTrades { get; set; }
    public int CompletedPairs { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal BuyTotal { get; set; }
    public decimal SellTotal { get; set; }
    public Dictionary<DateTime, decimal> DailyPnL { get; set; } = new();
    public Dictionary<string, int> StrategyBreakdown { get; set; } = new();

    public static PerformanceSummary Empty => new();
}
