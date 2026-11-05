using System;
using System.ComponentModel;
using System.Windows;

namespace MqttNotify.Windows;

public partial class MainWindow : Window
{
    private bool _closeAllowed;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>窗口被收进托盘时触发，用于首次提示用户「程序其实还在跑」。</summary>
    public event EventHandler? HiddenToTray;

    /// <summary>
    /// 放行关闭。退出程序前必须调用，否则会被「关闭即最小化到托盘」拦下来。
    /// </summary>
    public void AllowClose() => _closeAllowed = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        // 托盘常驻应用：点 X 只收进托盘，真正退出走托盘右键菜单。
        if (!_closeAllowed)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }
}
