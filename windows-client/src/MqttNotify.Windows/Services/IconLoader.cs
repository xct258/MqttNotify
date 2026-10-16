using System;
using System.Drawing;
using System.IO;

namespace MqttNotify.Windows.Services;

/// <summary>
/// 加载随程序发布的图标文件。
///
/// 找不到或读不出来时返回 <c>null</c>，由调用方回退到系统默认图标——
/// 图标缺失不该阻断启动。
/// </summary>
public static class IconLoader
{
    public const string IconFileName = "MqttNotify.ico";

    public static Icon? TryLoadTrayIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, IconFileName);
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
