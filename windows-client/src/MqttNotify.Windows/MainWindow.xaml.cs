using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MqttNotify.Windows.Services;

namespace MqttNotify.Windows;

public partial class MainWindow : Window
{
    private bool _closeAllowed;

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        SourceInitialized += (_, _) => WindowMaterial.TryApplyMica(this);
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        UpdateMaximizeGlyph();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 鼠标已经松开时 DragMove 会抛，忽略即可
            }
        }
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        DashboardPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void HideSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Visible;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void UpdateMaximizeGlyph() =>
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    /// <summary>
    /// 小屏幕 / 高 DPI 下 1040×720 可能超出工作区；按当前屏幕可用区域收缩，
    /// 同时允许用户继续手动调整大小。
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;

        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.90);
        MinWidth = Math.Min(MinWidth, area.Width * 0.80);
        MinHeight = Math.Min(MinHeight, area.Height * 0.80);
        MaxWidth = area.Width;
        MaxHeight = area.Height;
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
