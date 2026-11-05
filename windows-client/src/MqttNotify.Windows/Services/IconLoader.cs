using System;
using System.Drawing;
using System.IO;
using System.Windows;

namespace MqttNotify.Windows.Services;

/// <summary>
/// 加载托盘图标。
///
/// 优先从当前 exe 内嵌的图标资源读取（单文件发布时磁盘上只有 exe，
/// 不能再依赖 Assets/MqttNotify.ico 文件）；开发环境 / 资源读取失败时
/// 再回退到 pack 资源或磁盘文件。全部失败才返回 null，由调用方回退系统图标。
/// </summary>
public static class IconLoader
{
    public const string IconFileName = "MqttNotify.ico";

    private const string PackUri = "pack://application:,,,/Assets/MqttNotify.ico";

    public static Icon? TryLoadTrayIcon()
    {
        // 1. 单文件 / 普通发布：exe 本身由 ApplicationIcon 嵌入了图标
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) &&
                !Path.GetFileNameWithoutExtension(processPath)
                    .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var icon = Icon.ExtractAssociatedIcon(processPath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch (Exception)
        {
            // 继续尝试后面的方案
        }

        // 2. 开发环境 / 资源编译进程序集：从 WPF pack 资源读取
        try
        {
            var resource = Application.GetResourceStream(new Uri(PackUri, UriKind.Absolute));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream);
            }
        }
        catch (Exception)
        {
            // 继续尝试磁盘文件
        }

        // 3. 旧布局兜底：程序目录或 Assets 子目录下的 ico
        try
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(AppContext.BaseDirectory, IconFileName),
                         Path.Combine(AppContext.BaseDirectory, "Assets", IconFileName),
                     })
            {
                if (File.Exists(candidate))
                {
                    return new Icon(candidate);
                }
            }
        }
        catch (Exception)
        {
            // 图标缺失不阻断启动
        }

        return null;
    }
}
