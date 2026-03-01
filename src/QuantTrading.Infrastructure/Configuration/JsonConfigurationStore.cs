using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using QuantTrading.Core.Interfaces;
using QuantTrading.Core.Models;

namespace QuantTrading.Infrastructure.Configuration;

/// <summary>
/// JSON 檔案式組態持久化 — 類似輕量資料庫。
/// 預設存放於 Data/trading-config.json。
/// 線程安全，支援並行讀寫。
/// </summary>
public class JsonConfigurationStore : IConfigurationStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TimeSpanJsonConverter() }
    };

    public JsonConfigurationStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            AppContext.BaseDirectory, "Data", "trading-config.json");
    }

    /// <inheritdoc />
    public async Task<TradingConfiguration> LoadAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(_filePath))
            {
                // 第一次啟動 — 建立預設組態檔
                var defaults = new TradingConfiguration();
                await WriteInternalAsync(defaults);
                return defaults;
            }

            var json = await File.ReadAllTextAsync(_filePath);
            var dto = JsonSerializer.Deserialize<ConfigDto>(json, JsonOptions);
            if (dto == null) return new TradingConfiguration();

            var config = new TradingConfiguration
            {
                GapConfig = new PreMarketGapConfig
                {
                    MonitorStart = dto.GapConfig?.MonitorStart ?? new TimeSpan(8, 30, 0),
                    MonitorEnd = dto.GapConfig?.MonitorEnd ?? new TimeSpan(8, 59, 55),
                    ExecutionTime = dto.GapConfig?.ExecutionTime ?? new TimeSpan(9, 0, 0),
                    GapStrengthPercent = dto.GapConfig?.GapStrengthPercent ?? 0.01m,
                    FakeoutPullbackPercent = dto.GapConfig?.FakeoutPullbackPercent ?? 0.005m,
                    StopLossOffsetPercent = dto.GapConfig?.StopLossOffsetPercent ?? 0.01m,
                },
                DipConfig = new IntradayDipConfig
                {
                    ActiveStart = dto.DipConfig?.ActiveStart ?? new TimeSpan(9, 1, 0),
                    ActiveEnd = dto.DipConfig?.ActiveEnd ?? new TimeSpan(13, 25, 0),
                    DipThresholdPercent = dto.DipConfig?.DipThresholdPercent ?? 0.02m,
                    VolumeSpikeMultiplier = dto.DipConfig?.VolumeSpikeMultiplier ?? 3.0,
                    VolumeLookbackBars = dto.DipConfig?.VolumeLookbackBars ?? 5,
                    StopLossOffsetPercent = dto.DipConfig?.StopLossOffsetPercent ?? 0.01m,
                },
                RiskConfig = new RiskConfig
                {
                    RiskPerTrade = dto.RiskConfig?.RiskPerTrade ?? 1100m,
                    MaxDailyLoss = dto.RiskConfig?.MaxDailyLoss ?? 5000m,
                    MaxDailyTrades = dto.RiskConfig?.MaxDailyTrades ?? 5,
                }
            };

            return config;
        }
        catch (JsonException)
        {
            // 檔案損壞 — 備份後回傳預設值並覆寫修復
            try
            {
                var backupPath = _filePath + $".corrupt.{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                File.Copy(_filePath, backupPath);
            }
            catch { /* best-effort backup */ }

            var defaults = new TradingConfiguration();
            await WriteInternalAsync(defaults);
            return defaults;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(TradingConfiguration config)
    {
        await _semaphore.WaitAsync();
        try
        {
            await WriteInternalAsync(config);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WriteInternalAsync(TradingConfiguration config)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var dto = new ConfigDto
        {
            GapConfig = new GapConfigDto
            {
                MonitorStart = config.GapConfig.MonitorStart,
                MonitorEnd = config.GapConfig.MonitorEnd,
                ExecutionTime = config.GapConfig.ExecutionTime,
                GapStrengthPercent = config.GapConfig.GapStrengthPercent,
                FakeoutPullbackPercent = config.GapConfig.FakeoutPullbackPercent,
                StopLossOffsetPercent = config.GapConfig.StopLossOffsetPercent,
            },
            DipConfig = new DipConfigDto
            {
                ActiveStart = config.DipConfig.ActiveStart,
                ActiveEnd = config.DipConfig.ActiveEnd,
                DipThresholdPercent = config.DipConfig.DipThresholdPercent,
                VolumeSpikeMultiplier = config.DipConfig.VolumeSpikeMultiplier,
                VolumeLookbackBars = config.DipConfig.VolumeLookbackBars,
                StopLossOffsetPercent = config.DipConfig.StopLossOffsetPercent,
            },
            RiskConfig = new RiskConfigDto
            {
                RiskPerTrade = config.RiskConfig.RiskPerTrade,
                MaxDailyLoss = config.RiskConfig.MaxDailyLoss,
                MaxDailyTrades = config.RiskConfig.MaxDailyTrades,
            },
            LastModified = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        // Atomic write: 先寫入暫存檔再 rename，避免寫入中際程序中斷導致檔案損毀
        var tmpPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, _filePath, overwrite: true);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _semaphore.Dispose();
            _disposed = true;
        }
    }

    // ── JSON DTO(s) ─ 確保 TimeSpan 以 "HH:mm:ss" 格式存儲 ────────

    private class ConfigDto
    {
        public GapConfigDto? GapConfig { get; set; }
        public DipConfigDto? DipConfig { get; set; }
        public RiskConfigDto? RiskConfig { get; set; }
        public DateTime? LastModified { get; set; }
    }

    private class GapConfigDto
    {
        public TimeSpan MonitorStart { get; set; }
        public TimeSpan MonitorEnd { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public decimal GapStrengthPercent { get; set; }
        public decimal FakeoutPullbackPercent { get; set; }
        public decimal StopLossOffsetPercent { get; set; }
    }

    private class DipConfigDto
    {
        public TimeSpan ActiveStart { get; set; }
        public TimeSpan ActiveEnd { get; set; }
        public decimal DipThresholdPercent { get; set; }
        public double VolumeSpikeMultiplier { get; set; }
        public int VolumeLookbackBars { get; set; }
        public decimal StopLossOffsetPercent { get; set; }
    }

    private class RiskConfigDto
    {
        public decimal RiskPerTrade { get; set; }
        public decimal MaxDailyLoss { get; set; }
        public int MaxDailyTrades { get; set; }
    }
}

/// <summary>
/// TimeSpan ↔ "HH:mm:ss" JSON 轉換器。
/// </summary>
internal class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return TimeSpan.TryParse(str, out var ts) ? ts : TimeSpan.Zero;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(@"hh\:mm\:ss"));
    }
}
