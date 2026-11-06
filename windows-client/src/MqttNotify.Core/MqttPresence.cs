using System;
using System.Text.Json;

namespace MqttNotify.Core;

/// <summary>
/// 客户端在线状态上报。
///
/// 约定：连接成功后向 <c>$presence/clients/{client_id}</c> 发布 retained JSON，
/// 断开时由 MQTT Will（LWT）把 <c>online</c> 改成 false；连接期间每 60 秒刷新一次。
/// 主题以 <c>$</c> 开头，不会被业务订阅的 <c>#</c> 通配符匹配到。
/// </summary>
public static class MqttPresence
{
    public const string TopicPrefix = "$presence/clients";

    public const string Platform = "windows";

    public static string TopicFor(string clientId) =>
        $"{TopicPrefix}/{clientId.Trim().Replace('/', '_')}";

    public static string SuffixFor(string clientId)
    {
        var id = clientId.Trim();
        var separator = id.LastIndexOf('_');
        return separator >= 0 && separator < id.Length - 1 ? id[(separator + 1)..] : id;
    }

    public static byte[] Payload(string clientId, string topic, bool online) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            client_id = clientId,
            suffix = SuffixFor(clientId),
            topic,
            name = Environment.MachineName,
            platform = Platform,
            version = AppVersion,
            online,
            ts = DateTimeOffset.Now.ToString("O"),
        });

    public static string AppVersion =>
        typeof(MqttPresence).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
