using System.Globalization;
using System.Text;

namespace MqttNotify.Core;

/// <summary>
/// 下行消息解析，与手机端 <c>MqttMessageParser</c> 保持完全一致的口径。
///
/// push-server 约定 payload 为 <b>拼好的纯文本</b>，格式 <c>【待办提醒】{title}\n{message}</c>：
/// <list type="bullet">
///   <item>首行若以 <c>【待办提醒】</c> 开头，其后即标题，剩余行为正文；</item>
///   <item>无该前缀时整段作为正文，标题使用 <paramref name="fallbackTitle"/>；</item>
///   <item>主题最后一段即 task_id（取不到则为 <c>null</c>，仅用于展示/去重）。</item>
/// </list>
///
/// 解析失败不抛异常：宁可展示原文，也不丢提醒。
/// </summary>
public static class MqttMessageParser
{
    public const string Marker = "【待办提醒】";

    public sealed record ParsedAlert(int? TaskId, string Title, string Body);

    public static ParsedAlert Parse(string topic, byte[] payload, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(payload);

        // 统一换行符：服务端发 \n，但兼容 \r\n 更稳
        var raw = Encoding.UTF8.GetString(payload).Replace("\r\n", "\n").Trim();
        var taskId = ParseTaskId(topic);

        if (raw.Length == 0)
        {
            return new ParsedAlert(taskId, fallbackTitle, string.Empty);
        }

        var lines = raw.Split('\n');
        var firstLine = lines[0];

        if (firstLine.StartsWith(Marker, StringComparison.Ordinal))
        {
            var title = firstLine[Marker.Length..].Trim();
            var body = string.Join('\n', lines.Skip(1)).Trim();
            return new ParsedAlert(taskId, title.Length == 0 ? fallbackTitle : title, body);
        }

        return new ParsedAlert(taskId, fallbackTitle, raw);
    }

    /// <summary>
    /// 主题尾段转 task_id。刻意不用 <c>int.TryParse</c> 的默认样式，
    /// 免得把 <c>" 7 "</c> 这种带空白的野数据也当成合法 id（与 Java 的 <c>toIntOrNull</c> 对齐）。
    /// </summary>
    private static int? ParseTaskId(string topic)
    {
        var lastSegment = topic[(topic.LastIndexOf('/') + 1)..];
        return int.TryParse(
            lastSegment,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var id)
            ? id
            : null;
    }
}
