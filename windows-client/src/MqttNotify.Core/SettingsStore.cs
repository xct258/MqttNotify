using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MqttNotify.Core;

/// <summary>配置持久化。</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>默认配置文件位置：<c>%APPDATA%\MqttNotify\settings.json</c>。</summary>
public static class SettingsPaths
{
    public static string DefaultSettingsFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MqttNotify",
        "settings.json");

    public static string DefaultAlertsFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MqttNotify",
        "alerts.json");
}

/// <summary>
/// JSON 文件存储。
///
/// 写入走「临时文件 + 原子替换」，避免进程崩溃时把配置写成半截。
/// 读取失败（文件损坏 / 无权限）时降级为默认配置，不阻断启动——与手机端
/// 「DataStore 读损坏则回退空配置」的策略一致。
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                var defaults = new AppSettings().Normalized();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_filePath, Encoding.UTF8);

            // AppSettings 的属性初始化器会给缺失的 ConfigVersion 填上当前值，
            // 所以必须先在 JSON 层确认这个字段真的存在，才能识别旧配置。
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("configVersion", out var version) ||
                !version.TryGetInt32(out var value) ||
                value != AppSettings.CurrentConfigVersion)
            {
                var defaults = new AppSettings().Normalized();
                Save(defaults);
                return defaults;
            }

            var settings = document.RootElement.Deserialize<AppSettings>(Options) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.ClientIdSuffix))
            {
                // 第一次升级到「固定随机后缀」时生成一次并写回，之后重启保持不变。
                settings = settings.Normalized();
                Save(settings);
            }
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings.Normalized(), Options);
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, json, Encoding.UTF8);
        File.Move(temp, _filePath, overwrite: true);
    }
}
