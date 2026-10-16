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

/// <summary>收到的提醒（仅内存保留，不落盘；与手机端首版一致）。</summary>
public sealed record AlertRecord(
    int? TaskId,
    string Topic,
    string Title,
    string Body,
    DateTimeOffset ReceivedAt);
