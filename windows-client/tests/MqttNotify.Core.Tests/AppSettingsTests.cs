using Xunit;

namespace MqttNotify.Core.Tests;

/// <summary>
/// 配置模型的契约测试，与手机端 <c>AppSettingsTest</c> 对应。
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void 默认与push_server文档一致()
    {
        var settings = new AppSettings();

        Assert.Equal(1883, settings.Port);
        Assert.Equal("taskops/todo", settings.TopicBase);
        Assert.Equal("taskops/todo/#", settings.SubscribeTopic);
        Assert.Equal(MqttTransport.Tcp, settings.Transport);
    }

    [Fact]
    public void 订阅主题拼接通配符并去掉多余斜杠()
    {
        Assert.Equal("a/b/#", new AppSettings { TopicBase = "a/b" }.SubscribeTopic);
        Assert.Equal("a/b/#", new AppSettings { TopicBase = "a/b/" }.SubscribeTopic);
        Assert.Equal("a/b/#", new AppSettings { TopicBase = "  a/b//  " }.SubscribeTopic);
    }

    [Fact]
    public void host为空时配置无效()
    {
        Assert.False(new AppSettings { Host = string.Empty }.IsValid);
        Assert.True(new AppSettings { Host = "192.168.1.10" }.IsValid);
    }

    [Fact]
    public void 端口越界时配置无效()
    {
        Assert.False(new AppSettings { Host = "h", Port = 0 }.IsValid);
        Assert.False(new AppSettings { Host = "h", Port = 70000 }.IsValid);
        Assert.True(new AppSettings { Host = "h", Port = 1 }.IsValid);
        Assert.True(new AppSettings { Host = "h", Port = 65535 }.IsValid);
    }

    [Fact]
    public void 主题为空时配置无效()
    {
        Assert.False(new AppSettings { Host = "h", TopicBase = "  " }.IsValid);
    }

    [Fact]
    public void normalized会去掉首尾空白()
    {
        var settings = new AppSettings
        {
            Host = "  10.0.0.1 ",
            TopicBase = " taskops/todo/ ",
            ServerName = " taskops ",
            ClientId = " cid ",
        }.Normalized();

        Assert.Equal("10.0.0.1", settings.Host);
        Assert.Equal("taskops/todo", settings.TopicBase);
        Assert.Equal("taskops", settings.ServerName);
        Assert.Equal("cid", settings.ClientId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mqtt")]
    public void wsPath为空或缺前导斜杠时补成合法路径(string input)
    {
        var settings = new AppSettings { WsPath = input };

        Assert.Equal("/mqtt", settings.EffectiveWsPath);
    }

    [Fact]
    public void wsPath已合法时原样保留()
    {
        Assert.Equal("/custom/path", new AppSettings { WsPath = "/custom/path" }.EffectiveWsPath);
    }

    [Fact]
    public void normalized会清掉与传输方式无关的跳过校验开关()
    {
        var tcp = new AppSettings { Transport = MqttTransport.Tcp, AllowInsecureTls = true }.Normalized();
        Assert.False(tcp.AllowInsecureTls);

        var wss = new AppSettings { Transport = MqttTransport.Wss, AllowInsecureTls = true }.Normalized();
        Assert.True(wss.AllowInsecureTls);
    }

    [Theory]
    [InlineData(MqttTransport.Tcp, "mqtt://h:1883")]
    [InlineData(MqttTransport.Ws, "ws://h:9001/mqtt")]
    [InlineData(MqttTransport.Wss, "wss://h:443/mqtt")]
    public void 展示地址按传输方式拼装(MqttTransport transport, string expected)
    {
        Assert.Equal(expected, transport.DisplayUrl("h", transport.DefaultPort(), "/mqtt"));
    }

    [Fact]
    public void 默认端口与push_server一致()
    {
        Assert.Equal(1883, MqttTransport.Tcp.DefaultPort());
        Assert.Equal(9001, MqttTransport.Ws.DefaultPort());
        Assert.Equal(443, MqttTransport.Wss.DefaultPort());

        Assert.False(MqttTransport.Tcp.IsWebSocket());
        Assert.True(MqttTransport.Ws.IsWebSocket());
        Assert.True(MqttTransport.Wss.IsTls());
    }
}
