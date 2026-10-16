using System;
using Microsoft.Win32;

namespace MqttNotify.Windows.Services;

/// <summary>
/// 开机自启：写当前用户的 Run 项 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>。
///
/// 用 HKCU 而不是 HKLM，因此不需要管理员权限，也不该写 HKLM。
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MqttNotify";

    /// <summary>启动项里带这个参数时，启动后直接进托盘、不弹主窗口。</summary>
    public const string MinimizedArgument = "--minimized";

    /// <summary>当前是否已登记自启。</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>登记 / 取消自启。</summary>
    public static void Apply(bool enabled, string executablePath, string? arguments = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($"无法写入注册表：HKCU\\{RunKeyPath}");

        if (enabled)
        {
            key.SetValue(
                ValueName,
                string.IsNullOrWhiteSpace(arguments) ? $"\"{executablePath}\"" : $"\"{executablePath}\" {arguments}",
                RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
