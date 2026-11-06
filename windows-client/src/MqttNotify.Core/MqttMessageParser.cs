using System.Globalization;
using System.Text;

namespace MqttNotify.Core;

/// <summary>下行消息分类，与 push-server 的 <c>category</c> / payload marker 对应。</summary>
public enum AlertKind
{
    Unknown = 0,
    Todo = 1,
    Service = 2,
}

/// <summary>
/// 下行消息解析，与手机端 <c>MqttMessageParser</c> 保持完全一致的口径。
///
/// 协议 v2：
/// <list type="bullet">
///   <item>待办：<c>【待办提醒】{title}\n{message}</c>；</item>
///   <item>服务：<c>【服务通知】{server}\n【服务】{title}\n【状态】{status}\n\n{message}</c>；</item>
///   <item>主题最后一段即 task_id（取不到则为 <c>null</c>，仅用于展示/去重）。</item>
/// </list>
/// 解析失败不抛异常：宁可展示原文，也不丢提醒。
/// </summary>
public static class MqttMessageParser
{
    public const string TodoMarker = "【待办提醒】";

    /// <summary>兼容旧调用方的名字。</summary>
    public const string Marker = TodoMarker;

    public const string ServiceMarker = "【服务通知】";
    public const string ServiceField = "【服务】";
    public const string StatusField = "【状态】";

    public sealed record ParsedAlert(
        AlertKind Kind,
        int? TaskId,
        string? ServerName,
        string? ServiceName,
        string? Status,
        string Title,
        string Body);

    public static ParsedAlert Parse(string topic, byte[] payload, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(payload);

        // 统一换行符：服务端发 \n，但兼容 \r\n 更稳
        var raw = Encoding.UTF8.GetString(payload).Replace("\r\n", "\n").Trim();
        var taskId = ParseTaskId(topic);

        if (raw.Length == 0)
        {
            return new ParsedAlert(
                AlertKind.Unknown, taskId, null, null, null, fallbackTitle, string.Empty);
        }

        var lines = raw.Split('\n');
        var firstLine = lines[0];

        if (firstLine.StartsWith(TodoMarker, StringComparison.Ordinal))
        {
            return ParseTodo(lines, taskId, fallbackTitle);
        }

        if (firstLine.StartsWith(ServiceMarker, StringComparison.Ordinal))
        {
            return ParseService(lines, taskId, fallbackTitle);
        }

        return new ParsedAlert(
            AlertKind.Unknown, taskId, null, null, null, fallbackTitle, raw);
    }

    private static ParsedAlert ParseTodo(string[] lines, int? taskId, string fallbackTitle)
    {
        var title = lines[0][TodoMarker.Length..].Trim();
        var body = string.Join('\n', lines.Skip(1)).Trim();
        return new ParsedAlert(
            AlertKind.Todo,
            taskId,
            null,
            null,
            null,
            title.Length == 0 ? fallbackTitle : title,
            body);
    }

    private static ParsedAlert ParseService(string[] lines, int? taskId, string fallbackTitle)
    {
        var server = lines[0][ServiceMarker.Length..].Trim();
        string? service = null;
        string? status = null;
        var bodyStart = 1;

        while (bodyStart < lines.Length)
        {
            var line = lines[bodyStart];
            if (string.IsNullOrWhiteSpace(line))
            {
                bodyStart++;
                break;
            }

            if (line.StartsWith(ServiceField, StringComparison.Ordinal))
            {
                var value = line[ServiceField.Length..].Trim();
                service = value.Length == 0 ? null : value;
            }
            else if (line.StartsWith(StatusField, StringComparison.Ordinal))
            {
                var value = line[StatusField.Length..].Trim();
                status = value.Length == 0 ? null : value;
            }
            else
            {
                // 没写空行时，遇到正文第一行就停，保证内容不丢
                break;
            }

            bodyStart++;
        }

        var body = string.Join('\n', lines.Skip(bodyStart)).Trim();
        var serverName = string.IsNullOrWhiteSpace(server) ? null : server;
        return new ParsedAlert(
            AlertKind.Service,
            taskId,
            serverName,
            service,
            status,
            serverName ?? fallbackTitle,
            body);
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
