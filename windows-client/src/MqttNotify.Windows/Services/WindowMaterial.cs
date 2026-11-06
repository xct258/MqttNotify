using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MqttNotify.Windows.Services;

/// <summary>
/// Windows 11 原生材质：Mica 背景、暗色标题栏、圆角窗口。
/// 低版本 Windows / 调用失败时保持 XAML 里的暗色纯色背景。
/// </summary>
public static class WindowMaterial
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaMicaEffect = 1029; // Windows 11 21H2 的旧属性

    private const int DwmsbtMainWindow = 2; // Mica
    private const int DwmcpRound = 2;

    /// <summary>给 WinForms 弹出菜单加暗色/圆角（Windows 11）。</summary>
    public static void ApplyDarkPopup(IntPtr handle)
    {
        if (handle == IntPtr.Zero || Environment.OSVersion.Version.Build < 22000)
        {
            return;
        }

        TrySet(handle, DwmwaUseImmersiveDarkMode, 1);
        TrySet(handle, DwmwaWindowCornerPreference, DwmcpRound);
    }

    public static bool TryApplyMica(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var build = Environment.OSVersion.Version.Build;
        if (build < 22000)
        {
            return false;
        }

        TrySet(handle, DwmwaUseImmersiveDarkMode, 1);
        TrySet(handle, DwmwaWindowCornerPreference, DwmcpRound);

        var mica = build >= 22621
            ? TrySet(handle, DwmwaSystemBackdropType, DwmsbtMainWindow)
            : TrySet(handle, DwmwaMicaEffect, 1);

        if (!mica)
        {
            return false;
        }

        // 不再扩展玻璃边框：WindowChrome 的自绘标题栏在扩展边框下会出现顶部
        // 额外空隙/分割线。Mica 作为窗口背景，配合半透明内容区即可生效。
        window.Background = Brushes.Transparent;
        return true;
    }

    private static bool TrySet(IntPtr handle, int attribute, int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

}
