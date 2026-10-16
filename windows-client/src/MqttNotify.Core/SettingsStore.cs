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
                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
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
