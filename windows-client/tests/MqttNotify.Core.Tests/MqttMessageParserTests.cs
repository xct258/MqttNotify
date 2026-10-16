using System.Text;
using Xunit;

namespace MqttNotify.Core.Tests;

/// <summary>
/// 下行 payload 解析的契约测试。
///
/// 对应 push-server 的 <c>downlink_text()</c>：
/// 有标题 -> <c>【待办提醒】{title}\n{message}</c>；无标题 -> 只发正文。
/// 主题尾段为 task_id。
///
/// 用例与手机端 <c>MqttMessageParserTest</c> 一一对应，保证两端解析口径一致。
/// </summary>
public class MqttMessageParserTests
{
    private const string Fallback = "新提醒";

    private static MqttMessageParser.ParsedAlert Parse(string topic, string payload) =>
        MqttMessageParser.Parse(topic, Encoding.UTF8.GetBytes(payload), Fallback);

    [Fact]
    public void 带标题与多行正文()
    {
        var alert = Parse("taskops/todo/7", "【待办提醒】交电费\n主卧电表，户号123456\n到期：2026-10-12");

        Assert.Equal(7, alert.TaskId);
        Assert.Equal("交电费", alert.Title);
        Assert.Equal("主卧电表，户号123456\n到期：2026-10-12", alert.Body);
    }

    [Fact]
    public void 只有标题无正文()
    {
        var alert = Parse("taskops/todo/8", "【待办提醒】交电费");

        Assert.Equal(8, alert.TaskId);
        Assert.Equal("交电费", alert.Title);
        Assert.Equal(string.Empty, alert.Body);
    }

    [Fact]
    public void 标题为空时回退到默认标题()
    {
        var alert = Parse("taskops/todo/9", "【待办提醒】\n正文在这里");

        Assert.Equal(Fallback, alert.Title);
        Assert.Equal("正文在这里", alert.Body);
    }

    [Fact]
    public void 无前缀时整段作为正文()
    {
        var alert = Parse("taskops/todo/10", "服务端只发了正文");

        Assert.Equal(Fallback, alert.Title);
        Assert.Equal("服务端只发了正文", alert.Body);
    }

    [Fact]
    public void 空payload不崩溃()
    {
        var alert = Parse("taskops/todo/11", string.Empty);

        Assert.Equal(Fallback, alert.Title);
        Assert.Equal(string.Empty, alert.Body);
    }

    [Fact]
    public void CRLF换行也能正确解析()
    {
        var alert = Parse("taskops/todo/12", "【待办提醒】交电费\r\n第一行\r\n第二行");

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
    }
}
