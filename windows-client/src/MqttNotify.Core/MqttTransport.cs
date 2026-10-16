namespace MqttNotify.Core;

/// <summary>
/// 下行传输方式，与手机端 <c>MqttTransport</c> 保持一致。
///
/// 对应 push-server 里 Mosquitto 的监听（见其 <c>容器构建脚本/start.sh</c>）：
/// <code>
/// listener 1883 127.0.0.1        # 只绑本机
///     allow_anonymous true
/// listener 9001 0.0.0.0
///     protocol websockets
///     allow_anonymous true
/// </code>
///
/// <list type="bullet">
///   <item><see cref="Tcp"/>：<c>mqtt://host:1883</c>，注意 1883 只绑 127.0.0.1，跨机连不上；</item>
///   <item><see cref="Ws"/>：<c>ws://host:9001</c>，push-server 默认对外开放的监听；</item>
///   <item><see cref="Wss"/>：<c>wss://host/mqtt</c>，WS 之上叠加 TLS，通常由反代终止 TLS。</item>
/// </list>
/// </summary>
public enum MqttTransport
{
    Tcp,
    Ws,
    Wss,
}

public static class MqttTransportExtensions
{
    /// <summary>URL scheme，也用于界面展示。</summary>
    public static string Scheme(this MqttTransport transport) => transport switch
    {
        MqttTransport.Tcp => "mqtt",
        MqttTransport.Ws => "ws",
        MqttTransport.Wss => "wss",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "未知的传输方式"),
    };

    /// <summary>推荐端口；用户切换传输方式且端口仍是旧默认值时会自动跟随。</summary>
    public static int DefaultPort(this MqttTransport transport) => transport switch
    {
        MqttTransport.Tcp => 1883,
        MqttTransport.Ws => 9001,
        MqttTransport.Wss => 443,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "未知的传输方式"),
    };

    public static bool IsWebSocket(this MqttTransport transport) =>
        transport is MqttTransport.Ws or MqttTransport.Wss;

    public static bool IsTls(this MqttTransport transport) => transport is MqttTransport.Wss;

    /// <summary>展示用连接地址，如 <c>wss://example.com:443/mqtt</c>、<c>mqtt://10.0.0.2:1883</c>。</summary>
    public static string DisplayUrl(this MqttTransport transport, string host, int port, string wsPath)
    {
        var displayHost = string.IsNullOrWhiteSpace(host) ? "<broker-host>" : host.Trim();
        if (!transport.IsWebSocket())
        {
            return $"{transport.Scheme()}://{displayHost}:{port}";
        }

        var path = wsPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return $"{transport.Scheme()}://{displayHost}:{port}{path}";
    }
}
