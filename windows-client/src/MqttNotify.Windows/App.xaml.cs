using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MqttNotify.Core;
using MqttNotify.Windows.Services;
using MqttNotify.Windows.ViewModels;

namespace MqttNotify.Windows;

/// <summary>
/// 应用入口：单实例守卫 + 依赖装配 + 托盘生命周期。
///
/// 刻意不引入 DI 容器：整个应用只有一条依赖链，手工装配更直观。
/// </summary>
public partial class App : Application
{
    /// <summary>Local\ 前缀 = 仅当前登录会话，多用户登录时各跑各的互不干扰。</summary>
    private const string SingleInstanceMutexName = @"Local\MqttNotify.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private MqttConnectionService? _connection;
    private TrayIconService? _tray;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryAcquireSingleInstance())
        {
            // 已经有实例在跑：直接退出。否则两个进程会用同一 ClientId 互相顶下线，并重复弹通知。
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var store = new JsonSettingsStore(SettingsPaths.DefaultSettingsFile);
        var settings = store.Load();

        _connection = new MqttConnectionService();
        _viewModel = new MainViewModel(_connection, store, settings);

        _tray = new TrayIconService(IconLoader.TryLoadTrayIcon());
        _tray.ShowWindowRequested += (_, _) => ShowMainWindow();
        _tray.StartRequested += (_, _) => { _ = _viewModel.StartAsync(); };
        _tray.StopRequested += (_, _) => { _ = _viewModel.StopAsync(); };
        _tray.ExitRequested += (_, _) => ExitApplication();

        _connection.StatusChanged += (_, snapshot) => _tray.UpdateStatus(snapshot.Status, snapshot.Detail);
        _viewModel.AlertRaised += (_, alert) => _tray.ShowAlert(alert.Title, alert.Body);

        // 状态可能在装配前就已变化，先把当前状态同步给托盘
        _tray.UpdateStatus(_viewModel.Status, _viewModel.StatusDetail);

        _window = new MainWindow { DataContext = _viewModel };
        _window.HiddenToTray += OnHiddenToTray;
        MainWindow = _window;

        if (!IsMinimizedStart(e.Args))
        {
            _window.Show();
        }

        // 配置合法就自动连上：托盘常驻应用不该还要用户手点一次「启动」
        _ = _viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 退出流程绝不能抛异常：收尾失败最多是连接没断开，不该让进程以崩溃退出
        try
        {
            _connection?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // 忽略：进程马上结束，MQTTnet 的连接会被 OS 回收
        }

        _tray?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        return createdNew;
    }

    /// <summary>启动项里带 <c>--minimized</c> 时直接进托盘（开机自启走这条）。</summary>
    private static bool IsMinimizedStart(string[] args) =>
        args.Any(a => string.Equals(a, AutostartService.MinimizedArgument, StringComparison.OrdinalIgnoreCase));

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        // 从托盘唤起时窗口常被别的应用压住，短暂置顶一次确保可见
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Activate();
        _window.Focus();
    }

    private void OnHiddenToTray(object? sender, EventArgs e)
    {
        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _tray?.ShowAlert("MQTT 提醒仍在运行", "窗口已收进托盘，双击图标可重新打开，右键菜单可退出。");
    }

    private void ExitApplication()
    {
        // 放行 MainWindow 的「关闭即最小化」逻辑，确保能真正退出
        _window?.AllowClose();
        Shutdown();
    }

    /// <summary>
    /// 界面线程兜底：托盘应用一旦崩溃是「静默消失」，比弹个错误框更糟。
    /// 这里标记为已处理，让进程继续跑，问题留在状态栏提示里。
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        System.Windows.MessageBox.Show(
            $"发生未处理的异常，程序会继续运行：\n\n{e.Exception.Message}",
            "MQTT 提醒",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
