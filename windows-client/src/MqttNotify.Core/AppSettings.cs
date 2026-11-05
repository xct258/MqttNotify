namespace MqttNotify.Core;

/// <summary>
/// 客户端连接配置，字段与手机端 <c>AppSettings</c> 一一对应。
///
/// <list type="bullet">
///   <item><see cref="Host"/> / <see cref="Port"/> / <see cref="Transport"/>：broker 地址与传输方式；</item>
///   <item><see cref="TopicBase"/>：下行主题的 base，默认 <c>taskops/todo</c>；实际订阅 <see cref="SubscribeTopic"/>；</item>
///   <item><see cref="ServerName"/>：来源服务名（如 taskops），预留给后续 REST 补拉；</item>
///   <item><see cref="ClientId"/>：自定义 Client ID 前缀；最终 ID 为 <c>{前缀}_{固定随机后缀}</c>，留空前缀时为 <c>windows_xxxxxxxx</c>；</item>
///   <item><see cref="WsPath"/>：WS/WSS 的 serverPath（默认 <c>/mqtt</c>）；</item>
///   <item><see cref="AllowInsecureTls"/>：WSS 下跳过证书链与主机名校验（仅内网自签证书用）。</item>
/// </list>
/// </summary>
public sealed record AppSettings
{
    /// <summary>配置格式版本。开发期不做迁移：版本不一致时直接回落到默认配置。</summary>
    public const int CurrentConfigVersion = 2;

    public const string DefaultHost = "push-server.o1-1.xct258.top";
    public const int DefaultPort = 443;
    public const string DefaultTopic = "taskops/todo";
    public const string DefaultServer = "taskops";
    public const string DefaultWsPath = "/mqtt";
    public const MqttTransport DefaultTransport = MqttTransport.Wss;

    public int ConfigVersion { get; init; } = CurrentConfigVersion;

    public string Host { get; init; } = DefaultHost;

    public int Port { get; init; } = DefaultPort;

    public string TopicBase { get; init; } = DefaultTopic;

    public string ServerName { get; init; } = DefaultServer;

    /// <summary>用户自定义的 Client ID 前缀；留空时使用 <c>windows</c>。</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>安装时生成并持久化的随机后缀，清空配置/重装后才会变化。</summary>
    public string ClientIdSuffix { get; init; } = string.Empty;

    /// <summary>最终 MQTT Client ID：<c>{自定义或 windows}_{固定随机后缀}</c>。</summary>
    public string EffectiveClientId
    {
        get
        {
            var prefix = string.IsNullOrWhiteSpace(ClientId) ? "windows" : ClientId.Trim();
            var suffix = ClientIdSuffix.Trim();
            return suffix.Length == 0 ? prefix : $"{prefix}_{suffix}";
        }
    }

    public static string NewClientIdSuffix() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>开机自启（写注册表 Run 项）。</summary>
    public bool AutoStartOnBoot { get; init; } = true;

    /// <summary>
    /// 默认走公网 WSS：push-server 的 Mosquitto 只把 TCP 1883 绑在 127.0.0.1，
    /// 跨机（含局域网）连不上；公网入口是 `wss://push-server.o1-1.xct258.top/mqtt`。
    /// </summary>
    public MqttTransport Transport { get; init; } = DefaultTransport;

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
        ClientIdSuffix = string.IsNullOrWhiteSpace(ClientIdSuffix) ? NewClientIdSuffix() : ClientIdSuffix.Trim(),
        WsPath = EffectiveWsPath,
        AllowInsecureTls = AllowInsecureTls && Transport.IsTls(),
    };
}
