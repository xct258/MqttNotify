using System.Text.Json.Serialization;

namespace MqttNotify.Core;

/// <summary>连接状态，与手机端 <c>MqttState.Status</c> 一致（展示用文案由界面层本地化）。</summary>
public enum ConnectionStatus
{
    Stopped,
    Connecting,
    Connected,
    Reconnecting,
    Failed,
}

/// <summary>连接状态快照：状态 + 一行说明（地址 / 错误原因）。</summary>
public sealed record ConnectionSnapshot(ConnectionStatus Status, string Detail)
{
    public static ConnectionSnapshot Stopped { get; } = new(ConnectionStatus.Stopped, string.Empty);

    public static ConnectionSnapshot Connecting(string detail) =>
        new(ConnectionStatus.Connecting, detail);

    public static ConnectionSnapshot Connected(string detail) =>
        new(ConnectionStatus.Connected, detail);

    public static ConnectionSnapshot Reconnecting(string detail) =>
        new(ConnectionStatus.Reconnecting, detail);

    public static ConnectionSnapshot Failed(string detail) =>
        new(ConnectionStatus.Failed, detail);
}

/// <summary>收到的提醒。落盘字段与手机端一致；Display* 仅用于界面绑定。</summary>
public sealed record AlertRecord(
    AlertKind Kind,
    int? TaskId,
    string? ServerName,
    string? ServiceName,
    string? Status,
    string Topic,
    string Title,
    string Body,
    DateTimeOffset ReceivedAt)
{
    /// <summary>卡片主标题：服务通知用服务器名，待办用待办标题。</summary>
    [JsonIgnore]
    public string DisplayTitle => Kind == AlertKind.Service ? (ServerName ?? Title) : Title;

    /// <summary>服务名称（仅服务通知卡片显示）。</summary>
    [JsonIgnore]
    public string? DisplayService => Kind == AlertKind.Service ? ServiceName : null;

    [JsonIgnore]
    public bool HasService => !string.IsNullOrWhiteSpace(DisplayService);

    [JsonIgnore]
    public bool IsService => Kind == AlertKind.Service;

    [JsonIgnore]
    public string? StatusLabel => Kind == AlertKind.Service ? Status?.Trim().ToUpperInvariant() : null;

    [JsonIgnore]
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusLabel);
}
