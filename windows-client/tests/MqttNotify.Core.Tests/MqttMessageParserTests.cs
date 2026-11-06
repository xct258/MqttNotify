using System.Text;
using Xunit;

namespace MqttNotify.Core.Tests;

/// <summary>
/// 下行 payload 解析的契约测试，与手机端 <c>MqttMessageParserTest</c> 一一对应。
///
/// - 待办：<c>【待办提醒】{title}\n{message}</c>
/// - 服务：<c>【服务通知】{server}\n【服务】{title}\n【状态】{status}\n\n{message}</c>
/// - 主题尾段为 task_id。
/// </summary>
public class MqttMessageParserTests
{
    private const string Fallback = "新提醒";

    private static MqttMessageParser.ParsedAlert Parse(string topic, string payload) =>
        MqttMessageParser.Parse(topic, Encoding.UTF8.GetBytes(payload), Fallback);

    [Fact]
    public void 待办_带标题与多行正文()
    {
        var alert = Parse("taskops/todo/7", "【待办提醒】交电费\n主卧电表，户号123456\n到期：2026-10-12");

        Assert.Equal(AlertKind.Todo, alert.Kind);
        Assert.Equal(7, alert.TaskId);
        Assert.Equal("交电费", alert.Title);
        Assert.Equal("主卧电表，户号123456\n到期：2026-10-12", alert.Body);
    }

    [Fact]
    public void 待办_只有标题无正文()
    {
        var alert = Parse("taskops/todo/8", "【待办提醒】交电费");

        Assert.Equal(AlertKind.Todo, alert.Kind);
        Assert.Equal(8, alert.TaskId);
        Assert.Equal("交电费", alert.Title);
        Assert.Equal(string.Empty, alert.Body);
    }

    [Fact]
    public void 待办_标题为空时回退到默认标题()
    {
        var alert = Parse("taskops/todo/9", "【待办提醒】\n正文在这里");

        Assert.Equal(AlertKind.Todo, alert.Kind);
        Assert.Equal(Fallback, alert.Title);
        Assert.Equal("正文在这里", alert.Body);
    }

    [Fact]
    public void 服务_完整头与Markdown正文()
    {
        var alert = Parse(
            "server/khx-live",
            "【服务通知】o1-debian-1\n【服务】录播备份上传脚本\n【状态】ok\n\n上传成功\n\n- 文件 A\n- 文件 B");

        Assert.Equal(AlertKind.Service, alert.Kind);
        Assert.Equal("o1-debian-1", alert.ServerName);
        Assert.Equal("录播备份上传脚本", alert.ServiceName);
        Assert.Equal("ok", alert.Status);
        Assert.Equal("o1-debian-1", alert.Title);
        Assert.Equal("上传成功\n\n- 文件 A\n- 文件 B", alert.Body);
        Assert.Null(alert.TaskId);
    }

    [Fact]
    public void 服务_没有状态行也能解析()
    {
        var alert = Parse("server/khx-live", "【服务通知】nas\n【服务】备份\n\n完成");

        Assert.Equal(AlertKind.Service, alert.Kind);
        Assert.Equal("nas", alert.ServerName);
        Assert.Equal("备份", alert.ServiceName);
        Assert.Null(alert.Status);
        Assert.Equal("完成", alert.Body);
    }

    [Fact]
    public void 服务_正文以字段样式开头也不会被吞掉()
    {
        var alert = Parse(
            "server/khx-live",
            "【服务通知】nas\n【服务】备份\n\n【服务】这行属于正文");

        Assert.Equal("【服务】这行属于正文", alert.Body);
    }

    [Fact]
    public void 服务_缺少空行时遇到正文立即停止解析头()
    {
        var alert = Parse("server/khx-live", "【服务通知】nas\n【服务】备份\n上传成功");

        Assert.Equal("nas", alert.ServerName);
        Assert.Equal("备份", alert.ServiceName);
        Assert.Equal("上传成功", alert.Body);
    }

    [Fact]
    public void 未知格式_整段作为正文()
    {
        var alert = Parse("taskops/todo/10", "服务端只发了正文");

        Assert.Equal(AlertKind.Unknown, alert.Kind);
        Assert.Equal(Fallback, alert.Title);
        Assert.Equal("服务端只发了正文", alert.Body);
    }

    [Fact]
    public void 空payload不崩溃()
    {
        var alert = Parse("taskops/todo/11", string.Empty);

        Assert.Equal(AlertKind.Unknown, alert.Kind);
        Assert.Equal(Fallback, alert.Title);
        Assert.Equal(string.Empty, alert.Body);
    }

    [Fact]
    public void CRLF换行也能正确解析()
    {
        var alert = Parse("taskops/todo/12", "【待办提醒】交电费\r\n第一行\r\n第二行");

        Assert.Equal(AlertKind.Todo, alert.Kind);
        Assert.Equal("交电费", alert.Title);
        Assert.Equal("第一行\n第二行", alert.Body);
    }

    [Fact]
    public void 主题尾段非数字时taskId为null()
    {
        var alert = Parse("taskops/todo/not-a-number", "【待办提醒】x");

        Assert.Null(alert.TaskId);
        Assert.Equal("x", alert.Title);
    }

    [Fact]
    public void 主题不含斜杠时取整段()
    {
        var alert = Parse("13", "【待办提醒】x");

        Assert.Equal(13, alert.TaskId);
    }

    [Theory]
    [InlineData(" 7 ")]
    [InlineData("7.0")]
    [InlineData("")]
    public void 尾段不是合法整数时taskId为null(string tail)
    {
        var alert = Parse($"taskops/todo/{tail}", "【待办提醒】x");

        Assert.Null(alert.TaskId);
    }

    [Fact]
    public void marker常量与push_server一致()
    {
        Assert.Equal("【待办提醒】", MqttMessageParser.Marker);
        Assert.Equal("【服务通知】", MqttMessageParser.ServiceMarker);
    }
}
