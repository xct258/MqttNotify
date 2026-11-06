using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MqttNotify.Core;

/// <summary>
/// 提醒历史持久化：保留最近 30 天，最多 2000 条。
/// 文件位于 <c>%APPDATA%\MqttNotify\alerts.json</c>，使用临时文件原子替换。
/// </summary>
public sealed class AlertStore
{
    public static TimeSpan Retention { get; } = TimeSpan.FromDays(30);

    public const int MaxRecords = 2000;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly object _lock = new();

    public AlertStore(string filePath) => _filePath = filePath;

    public IReadOnlyList<AlertRecord> Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return Array.Empty<AlertRecord>();
                }

                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var records = JsonSerializer.Deserialize<List<AlertRecord>>(json, Options) ?? new();
                return Prune(records);
            }
            catch (Exception)
            {
                // 文件损坏 / 无权限时不影响启动，回退为空历史
                return Array.Empty<AlertRecord>();
            }
        }
    }

    public void Save(IEnumerable<AlertRecord> records)
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(Prune(records), Options);
                var temp = _filePath + ".tmp";
                File.WriteAllText(temp, json, Encoding.UTF8);
                File.Move(temp, _filePath, overwrite: true);
            }
            catch (Exception)
            {
                // 历史落盘失败不影响提醒接收
            }
        }
    }

    private static List<AlertRecord> Prune(IEnumerable<AlertRecord> records)
    {
        var cutoff = DateTimeOffset.Now - Retention;
        return records
            .Where(record => record.ReceivedAt >= cutoff)
            .OrderByDescending(record => record.ReceivedAt)
            .Take(MaxRecords)
            .ToList();
    }
}
