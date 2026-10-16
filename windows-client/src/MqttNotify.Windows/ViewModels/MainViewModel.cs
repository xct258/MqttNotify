using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using MqttNotify.Core;
using MqttNotify.Windows.Infrastructure;
using MqttNotify.Windows.Services;

namespace MqttNotify.Windows.ViewModels;

/// <summary>传输方式的下拉项。</summary>
public sealed record TransportOption(MqttTransport Value, string Display, string Hint);

/// <summary>
/// 主界面状态与表单。
///
/// 与手机端一致的做法：表单是「配置的编辑副本」，点保存才写回配置并触发重连，
/// 不会被后台的状态刷新打断输入。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>最近提醒在内存里最多保留多少条（不落盘，与手机端首版一致）。</summary>
    private const int MaxAlerts = 100;

    private readonly MqttConnectionService _connection;
    private readonly ISettingsStore _store;

    /// <summary>事件都可能来自 MQTTnet 的后台线程，统一切回 UI 线程。</summary>
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private AppSettings _savedSettings = new();

    private string _host = string.Empty;
    private string _port = string.Empty;
    private string _topicBase = string.Empty;
    private string _serverName = string.Empty;
    private string _clientId = string.Empty;
    private string _wsPath = AppSettings.DefaultWsPath;
    private TransportOption _selectedTransport = null!;
    private bool _allowInsecureTls;
    private bool _autoStartOnBoot = true;
    private bool _minimizeToTrayOnClose = true;

    private ConnectionStatus _status = ConnectionStatus.Stopped;
    private string _statusDetail = string.Empty;
    private string _statusMessage = string.Empty;

    public MainViewModel(MqttConnectionService connection, ISettingsStore store, AppSettings settings)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        TransportOptions = new List<TransportOption>
        {
            new(MqttTransport.Tcp, "TCP", "mqtt://host:1883。注意 push-server 的 Mosquitto 把 1883 绑在 127.0.0.1，跨机连不上。"),
            new(MqttTransport.Ws, "WebSocket", "ws://host:9001。push-server 默认对外开放的监听，局域网首选。"),
            new(MqttTransport.Wss, "WebSocket + TLS", "wss://host:443/mqtt。需要自己在前面挂 TLS 终止（nginx/caddy）。"),
        };

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave, ReportError);
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsRunning, ReportError);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsRunning, ReportError);
        ClearAlertsCommand = new RelayCommand(() => Alerts.Clear());

        _connection.StatusChanged += OnStatusChanged;
        _connection.AlertReceived += OnAlertReceived;

        LoadForm(settings.Normalized());
    }

    /// <summary>提醒已加入列表（在 UI 线程触发），供托盘弹通知。</summary>
    public event EventHandler<AlertRecord>? AlertRaised;

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public RelayCommand ClearAlertsCommand { get; }

    public IReadOnlyList<TransportOption> TransportOptions { get; }

    public ObservableCollection<AlertRecord> Alerts { get; } = new();

    // ---------------------------------------------------------------------
    // 表单
    // ---------------------------------------------------------------------

    public string Host
    {
        get => _host;
        set
        {
            if (SetProperty(ref _host, value))
            {
                RaiseDerived();
            }
        }
    }

    public string Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
            {
                RaiseDerived();
            }
        }
    }

    public string TopicBase
    {
        get => _topicBase;
        set
        {
            if (SetProperty(ref _topicBase, value))
            {
                RaiseDerived();
            }
        }
    }

    public string ServerName
    {
        get => _serverName;
        set => SetProperty(ref _serverName, value);
    }

    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value);
    }

    public string WsPath
    {
        get => _wsPath;
        set
        {
            if (SetProperty(ref _wsPath, value))
            {
                RaiseDerived();
            }
        }
    }

    public TransportOption SelectedTransport
    {
        get => _selectedTransport;
        set
        {
            var previous = _selectedTransport;
            if (!SetProperty(ref _selectedTransport, value))
            {
                return;
            }

            // 端口还是旧方式的默认值（或为空）时才跟随切换，避免覆盖用户自定义端口
            if (string.IsNullOrWhiteSpace(Port) || ParsedPort == previous?.Value.DefaultPort())
            {
                Port = value.Value.DefaultPort().ToString(CultureInfo.InvariantCulture);
            }

            RaiseDerived();
        }
    }

    public bool AllowInsecureTls
    {
        get => _allowInsecureTls;
        set => SetProperty(ref _allowInsecureTls, value);
    }

    public bool AutoStartOnBoot
    {
        get => _autoStartOnBoot;
        set => SetProperty(ref _autoStartOnBoot, value);
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set => SetProperty(ref _minimizeToTrayOnClose, value);
    }

    public bool IsWebSocketTransport => SelectedTransport.Value.IsWebSocket();

    public bool IsTlsTransport => SelectedTransport.Value.IsTls();

    public string TransportHint => SelectedTransport.Hint;

    /// <summary>实时预览最终连接地址，避免用户填错了还需连一次才知道。</summary>
    public string EndpointPreview =>
        SelectedTransport.Value.DisplayUrl(Host, ParsedPort ?? AppSettings.DefaultPort, WsPath);

    public string SubscribeTopicPreview =>
        $"{TopicBase.Trim().TrimEnd('/')}/#";

    public int? ParsedPort =>
        int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(TopicBase) &&
        ParsedPort is >= 1 and <= 65535;

    // ---------------------------------------------------------------------
    // 状态
    // ---------------------------------------------------------------------

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public string StatusText => Status switch
    {
        ConnectionStatus.Stopped => "已停止",
        ConnectionStatus.Connecting => "正在连接",
        ConnectionStatus.Connected => "已连接",
        ConnectionStatus.Reconnecting => "重连中",
        ConnectionStatus.Failed => "连接失败",
        _ => Status.ToString(),
    };

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public bool IsRunning => Status != ConnectionStatus.Stopped;

    /// <summary>一行操作反馈（保存成功、启动失败等），由界面短暂展示。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasAlerts => Alerts.Count > 0;

    // ---------------------------------------------------------------------
    // 操作
    // ---------------------------------------------------------------------

    /// <summary>启动时调用：配置合法就自动连上（托盘常驻应用不该还要手点一次）。</summary>
    public async Task InitializeAsync()
    {
        if (_savedSettings.IsValid)
        {
            await _connection.ApplySettingsAsync(_savedSettings).ConfigureAwait(true);
        }
        else
        {
            ShowStatusMessage("尚未配置 Broker 地址");
        }
    }

    public async Task SaveAsync()
    {
        var settings = BuildSettings();
        _store.Save(settings);

        ApplyAutostart(settings.AutoStartOnBoot);

        _savedSettings = settings;

        // 规范化可能改写 wsPath / 清掉无关开关，回填给表单，让用户看到真实生效值
        WsPath = settings.EffectiveWsPath;
        AllowInsecureTls = settings.AllowInsecureTls;

        await _connection.ApplySettingsAsync(settings).ConfigureAwait(true);
        ShowStatusMessage("设置已保存");
    }

    public async Task StartAsync()
    {
        if (!_savedSettings.IsValid)
        {
            ShowStatusMessage("请先填写并保存 Broker 地址");
            return;
        }

        await _connection.ApplySettingsAsync(_savedSettings).ConfigureAwait(true);
    }

    public async Task StopAsync()
    {
        await _connection.StopAsync().ConfigureAwait(true);
        ShowStatusMessage("已停止接收");
    }

    // ---------------------------------------------------------------------
    // 内部
    // ---------------------------------------------------------------------

    private void LoadForm(AppSettings settings)
    {
        // 直接写字段，避免构造期间触发「切换传输方式自动改端口」的联动
        _selectedTransport = TransportOptions.FirstOrDefault(o => o.Value == settings.Transport) ?? TransportOptions[0];
        _host = settings.Host;
        _port = settings.Port.ToString(CultureInfo.InvariantCulture);
        _topicBase = settings.TopicBase;
        _serverName = settings.ServerName;
        _clientId = settings.ClientId;
        _wsPath = settings.EffectiveWsPath;
        _allowInsecureTls = settings.AllowInsecureTls;
        _autoStartOnBoot = settings.AutoStartOnBoot;
        _minimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        _savedSettings = settings;

        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(TopicBase));
        OnPropertyChanged(nameof(ServerName));
        OnPropertyChanged(nameof(ClientId));
        OnPropertyChanged(nameof(WsPath));
        OnPropertyChanged(nameof(SelectedTransport));
        OnPropertyChanged(nameof(AllowInsecureTls));
        OnPropertyChanged(nameof(AutoStartOnBoot));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        RaiseDerived();
    }

    private AppSettings BuildSettings() => new()
    {
        Host = Host,
        Port = ParsedPort ?? AppSettings.DefaultPort,
        TopicBase = TopicBase,
        ServerName = ServerName,
        ClientId = ClientId,
        AutoStartOnBoot = AutoStartOnBoot,
        MinimizeToTrayOnClose = MinimizeToTrayOnClose,
        Transport = SelectedTransport.Value,
        WsPath = WsPath,
        AllowInsecureTls = AllowInsecureTls,
    };

    private void ApplyAutostart(bool enabled)
    {
        try
        {
            // 用 dotnet run 调试时 ProcessPath 是 dotnet.exe，写进注册表只会自启失败，直接跳过
            var executable = Environment.ProcessPath;
            if (executable is null ||
                Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AutostartService.Apply(enabled, executable, AutostartService.MinimizedArgument);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsWebSocketTransport));
        OnPropertyChanged(nameof(IsTlsTransport));
        OnPropertyChanged(nameof(TransportHint));
        OnPropertyChanged(nameof(EndpointPreview));
        OnPropertyChanged(nameof(SubscribeTopicPreview));
        OnPropertyChanged(nameof(ParsedPort));
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnStatusChanged(object? sender, ConnectionSnapshot snapshot)
    {
        RunOnUiThread(() =>
        {
            Status = snapshot.Status;
            StatusDetail = snapshot.Detail;
        });
    }

    private void OnAlertReceived(object? sender, AlertRecord alert)
    {
        RunOnUiThread(() =>
        {
            Alerts.Insert(0, alert);
            while (Alerts.Count > MaxAlerts)
            {
                Alerts.RemoveAt(Alerts.Count - 1);
            }

            OnPropertyChanged(nameof(HasAlerts));
        });

        AlertRaised?.Invoke(this, alert);
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
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

    private void ShowStatusMessage(string message)
    {
        StatusMessage = message;
    }

    private void ReportError(Exception exception)
    {
        StatusMessage = "操作失败：" + exception.Message;
    }
}
