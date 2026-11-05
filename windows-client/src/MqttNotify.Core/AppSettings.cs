namespace MqttNotify.Core;

/// <summary>
/// 客户端连接配置，字段与手机端 <c>AppSettings</c> 一一对应。
///
/// <list type="bullet">
///   <item><see cref="Host"/> / <see cref="Port"/> / <see cref="Transport"/>：broker 地址与传输方式；</item>
///   <item><see cref="TopicBase"/>：下行主题的 base，默认 <c>taskops/todo</c>；实际订阅 <see cref="SubscribeTopic"/>；</item>
///   <item><see cref="ServerName"/>：来源服务名（如 taskops），预留给后续 REST 补拉；</item>
///   <item><see cref="ClientId"/>：MQTT Client ID，留空则启动时自动生成；同 ID 重复连接会顶掉旧连接；</item>
///   <item><see cref="WsPath"/>：WS/WSS 的 serverPath（默认 <c>/mqtt</c>）；</item>
///   <item><see cref="AllowInsecureTls"/>：WSS 下跳过证书链与主机名校验（仅内网自签证书用）。</item>
/// </list>
/// </summary>
public sealed record AppSettings
{
    public const string DefaultHost = "";
    public const int DefaultPort = 9001;
    public const string DefaultTopic = "taskops/todo";
    public const string DefaultServer = "taskops";
    public const string DefaultWsPath = "/mqtt";

    public string Host { get; init; } = DefaultHost;

    public int Port { get; init; } = DefaultPort;

    public string TopicBase { get; init; } = DefaultTopic;

    public string ServerName { get; init; } = DefaultServer;

    public string ClientId { get; init; } = string.Empty;

    /// <summary>开机自启（写注册表 Run 项）。</summary>
    public bool AutoStartOnBoot { get; init; } = true;

    /// <summary>点关闭按钮时收进托盘而不是退出（托盘常驻应用的默认行为）。</summary>
    public bool MinimizeToTrayOnClose { get; init; } = true;

    /// <summary>
    /// 默认走 WebSocket：push-server 的 Mosquitto 只把 TCP 1883 绑在 127.0.0.1，
    /// 跨机（含局域网）连不上；9001 才是对外监听的端口。
    /// </summary>
    public MqttTransport Transport { get; init; } = MqttTransport.Ws;

    public string WsPath { get; init; } = DefaultWsPath;

    public bool AllowInsecureTls { get; init; }

    /// <summary>
    /// WebSocket 路径规范化：空则回落到 <c>/mqtt</c>，缺前导斜杠则补上。
    /// （MQTTnet 与反代都要求路径以 <c>/</c> 开头。）
    /// </summary>
    public string EffectiveWsPath
    {
        get
        {
            var path = WsPath.Trim();
            if (path.Length == 0)
            {
                return DefaultWsPath;
            }

            return path.StartsWith('/') ? path : "/" + path;
        }
    }

    /// <summary>
    /// 规范化订阅主题的 base：
    /// 既接受 <c>taskops/todo</c>，也接受用户直接粘贴 <c>taskops/todo/#</c>，
    /// 避免再拼一次变成非法的 <c>taskops/todo/#/#</c>。
    /// 单独的 <c>#</c> 表示订阅所有主题，原样保留。
    /// </summary>
    public static string NormalizeTopicBase(string topicBase)
    {
        var topic = topicBase.Trim().TrimEnd('/');

        if (topic == "#")
        {
            return "#";
        }

        if (topic.EndsWith("/#", StringComparison.Ordinal))
        {
            topic = topic[..^2].TrimEnd('/');
        }

        return topic;
    }

    /// <summary>根据 base 生成实际订阅的通配主题。</summary>
    public static string BuildSubscribeTopic(string topicBase)
    {
        var topic = NormalizeTopicBase(topicBase);
        return topic.Length == 0 || topic == "#" ? "#" : $"{topic}/#";
    }

    /// <summary>通配订阅主题，例如 <c>taskops/todo/#</c>；base 填 <c>#</c> 时为 <c>#</c>。</summary>
    public string SubscribeTopic => BuildSubscribeTopic(TopicBase);

    /// <summary>是否已配置到可连接的程度。</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Host) &&
        Port is >= 1 and <= 65535 &&
        !string.IsNullOrWhiteSpace(TopicBase);

    /// <summary>规范化：去空白、补 wsPath、清掉与传输方式无关的开关。</summary>
    public AppSettings Normalized() => this with
    {
        Host = Host.Trim(),
        TopicBase = NormalizeTopicBase(TopicBase),
        ServerName = ServerName.Trim(),
        ClientId = ClientId.Trim(),
        WsPath = EffectiveWsPath,
        AllowInsecureTls = AllowInsecureTls && Transport.IsTls(),
    };
}
