using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Infrastructure.Configuration;

/// <summary>
/// JSON 檔案式交易日誌 — 每日實際成交紀錄的持久化存儲。
/// 預設存放於 Data/trade-journal.json。
/// </summary>
public class JsonTradeJournalStore : ITradeJournalStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonTradeJournalStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            AppContext.BaseDirectory, "Data", "trade-journal.json");
    }

    /// <inheritdoc />
    public async Task<List<TradeRecord>> GetAllAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return await ReadInternalAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<List<TradeRecord>> GetByDateAsync(DateTime date)
    {
        await _semaphore.WaitAsync();
        try
        {
            var all = await ReadInternalAsync();
            return all.Where(r => r.TradeDate.Date == date.Date)
                      .OrderByDescending(r => r.CreatedAt)
                      .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(TradeRecord record)
    {
        await _semaphore.WaitAsync();
        try
        {
            var records = await ReadInternalAsync();
            records.Add(record);
            await WriteInternalAsync(records);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(TradeRecord record)
    {
        await _semaphore.WaitAsync();
        try
        {
            var records = await ReadInternalAsync();
            var idx = records.FindIndex(r => r.Id == record.Id);
            if (idx >= 0)
            {
                records[idx] = record;
                await WriteInternalAsync(records);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var records = await ReadInternalAsync();
            var removed = records.RemoveAll(r => r.Id == id);
            if (removed > 0)
                await WriteInternalAsync(records);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // ── Internal I/O ────────────────────────────────────────────────

    private async Task<List<TradeRecord>> ReadInternalAsync()
    {
        if (!File.Exists(_filePath))
            return new List<TradeRecord>();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<TradeRecord>>(json, JsonOptions)
                   ?? new List<TradeRecord>();
        }
        catch (JsonException)
        {
            return new List<TradeRecord>();
        }
    }

    private async Task WriteInternalAsync(List<TradeRecord> records)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(records, JsonOptions);

        // Atomic write: 先寫入暫存檔再 rename，避免寫入中際檔案損毀
        var tmpPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, _filePath, overwrite: true);
    }

    // ── CSV Export ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string> ExportCsvAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            var records = await ReadInternalAsync();

            if (fromDate.HasValue)
                records = records.Where(r => r.TradeDate.Date >= fromDate.Value.Date).ToList();
            if (toDate.HasValue)
                records = records.Where(r => r.TradeDate.Date <= toDate.Value.Date).ToList();

            records = records.OrderBy(r => r.TradeDate).ThenBy(r => r.CreatedAt).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Id,TradeDate,Ticker,Direction,Price,Quantity,Amount,Strategy,Note,CreatedAt");

            foreach (var r in records)
            {
                sb.Append(CsvEscape(r.Id)).Append(',');
                sb.Append(r.TradeDate.ToString("yyyy-MM-dd")).Append(',');
                sb.Append(CsvEscape(r.Ticker)).Append(',');
                sb.Append(r.Direction).Append(',');
                sb.Append(r.Price).Append(',');
                sb.Append(r.Quantity).Append(',');
                sb.Append(r.Amount).Append(',');
                sb.Append(CsvEscape(r.Strategy ?? "")).Append(',');
                sb.Append(CsvEscape(r.Note ?? "")).Append(',');
                sb.AppendLine(r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            return sb.ToString();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>CSV 欄位跳脫：包含逗號、引號或換行時用雙引號包裹。</summary>
    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _semaphore.Dispose();
            _disposed = true;
        }
    }
}
