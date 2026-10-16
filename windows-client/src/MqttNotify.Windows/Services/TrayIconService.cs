using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using MqttNotify.Core;

namespace MqttNotify.Windows.Services;

/// <summary>
/// 托盘图标：图标本体 + 右键菜单 + 气泡通知。
///
/// 为什么用 WinForms 的 <see cref="NotifyIcon"/>：WPF 自身没有托盘 API，
/// 而 NotifyIcon 是框架自带、零第三方依赖的最轻方案。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    /// <summary>NotifyIcon.Text 上限 63 字符，超了会抛异常。</summary>
    private const int TooltipMaxLength = 63;

    /// <summary>BalloonTipTitle 上限 63、BalloonTipText 上限 255 字符。</summary>
    private const int BalloonTitleMaxLength = 63;
    private const int BalloonTextMaxLength = 255;

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;

    /// <summary>
    /// 连接状态与提醒都来自 MQTTnet 的后台线程，而 NotifyIcon / ToolStripMenuItem 是
    /// WinForms 组件、只能在创建它们的 UI 线程上访问，所以这里统一 marshal 回 UI 线程。
    /// 构造函数在 UI 线程上执行，因此此处取到的就是 UI 线程的 Dispatcher。
    /// </summary>
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private bool _disposed;

    public TrayIconService(Icon? icon = null)
    {
        _statusItem = new ToolStripMenuItem("状态：已停止") { Enabled = false };
        _startItem = new ToolStripMenuItem("启动接收");
        _stopItem = new ToolStripMenuItem("停止接收") { Enabled = false };
        var showItem = new ToolStripMenuItem("显示主界面");
        var exitItem = new ToolStripMenuItem("退出");

        _startItem.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _stopItem.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        showItem.Click += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            new ToolStripSeparator(),
            _startItem,
            _stopItem,
            new ToolStripSeparator(),
            showItem,
            exitItem,
        });

        _notifyIcon = new NotifyIcon
        {
            // 没自带图标时退回系统默认图标，至少不是空图标
            Icon = icon ?? SystemIcons.Application,
            Text = "MQTT 提醒",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ShowWindowRequested;

    public event EventHandler? StartRequested;

    public event EventHandler? StopRequested;

    public event EventHandler? ExitRequested;

    /// <summary>把连接状态同步到托盘提示与菜单可用性。</summary>
    public void UpdateStatus(ConnectionStatus status, string detail) =>
        RunOnUiThread(() => ApplyStatus(status, detail));

    /// <summary>弹通知气泡。Win10/11 上由系统渲染成通知。</summary>
    public void ShowAlert(string title, string body) =>
        RunOnUiThread(() => ApplyAlert(title, body));

    private void ApplyStatus(ConnectionStatus status, string detail)
    {
        var label = status switch
        {
            ConnectionStatus.Stopped => "已停止",
            ConnectionStatus.Connecting => "正在连接",
            ConnectionStatus.Connected => "已连接",
            ConnectionStatus.Reconnecting => "重连中",
            ConnectionStatus.Failed => "连接失败",
            _ => status.ToString(),
        };

        _statusItem.Text = $"状态：{label}";
        _startItem.Enabled = status == ConnectionStatus.Stopped || status == ConnectionStatus.Failed;
        _stopItem.Enabled = status != ConnectionStatus.Stopped;

        var tooltip = string.IsNullOrWhiteSpace(detail) ? $"MQTT 提醒 · {label}" : $"MQTT 提醒 · {label} · {detail}";
        _notifyIcon.Text = Truncate(tooltip, TooltipMaxLength);
    }

    private void ApplyAlert(string title, string body)
    {
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.BalloonTipTitle = Truncate(title, BalloonTitleMaxLength);
        _notifyIcon.BalloonTipText = Truncate(body, BalloonTextMaxLength);
        _notifyIcon.ShowBalloonTip(10_000);
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
