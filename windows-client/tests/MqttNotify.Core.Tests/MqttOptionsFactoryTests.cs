using System.Net;
using MQTTnet.Client;
using MQTTnet.Formatter;
using Xunit;

namespace MqttNotify.Core.Tests;

/// <summary>
/// 「传输方式 × 是否信任自签证书」矩阵的单元测试。
///
/// WSS 需要真实 TLS 反代才能端到端跑通，沙箱里起不了 WSS broker，
/// 所以这条链路必须靠这一层把参数拼装钉死。
/// </summary>
public class MqttOptionsFactoryTests
{
    private static MqttClientOptions Build(
        MqttTransport transport,
        bool allowInsecureTls = false,
        string wsPath = "/mqtt",
        int? port = null)
    {
        var settings = new AppSettings
        {
            Host = "broker.local",
            Port = port ?? transport.DefaultPort(),
            Transport = transport,
            WsPath = wsPath,
            AllowInsecureTls = allowInsecureTls,
        };

        return MqttOptionsFactory.Build(settings, "cid");
    }

    [Fact]
    public void 通用参数与push_server文档一致()
    {
        var options = Build(MqttTransport.Tcp);

        Assert.Equal("cid", options.ClientId);
        Assert.Equal(MqttProtocolVersion.V311, options.ProtocolVersion);
        Assert.True(options.CleanSession);
        Assert.Equal(TimeSpan.FromSeconds(60), options.KeepAlivePeriod);
        Assert.Equal(MqttOptionsFactory.ConnectTimeout, options.Timeout);
    }

    [Fact]
    public void TCP走TCP通道()
    {
        var options = Build(MqttTransport.Tcp);
        var tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);

        // Server / Port 在 4.3.7 已过时，实际承载值是 RemoteEndpoint
        var endpoint = Assert.IsType<DnsEndPoint>(tcp.RemoteEndpoint);
        Assert.Equal("broker.local", endpoint.Host);
        Assert.Equal(1883, endpoint.Port);
        Assert.NotEqual(true, tcp.TlsOptions?.UseTls);
    }

    [Fact]
    public void WS走WebSocket通道且不带TLS()
    {
        var options = Build(MqttTransport.Ws);
        var ws = Assert.IsType<MqttClientWebSocketOptions>(options.ChannelOptions);

        Assert.Equal("ws://broker.local:9001/mqtt", ws.Uri);
        Assert.NotEqual(true, ws.TlsOptions?.UseTls);
    }

    [Fact]
    public void WSS走WebSocket通道并启用TLS()
    {
        var options = Build(MqttTransport.Wss);
        var ws = Assert.IsType<MqttClientWebSocketOptions>(options.ChannelOptions);

        Assert.Equal("wss://broker.local:443/mqtt", ws.Uri);
        Assert.True(ws.TlsOptions?.UseTls);
    }

    [Fact]
    public void WSS默认严格校验证书()
    {
        var ws = Assert.IsType<MqttClientWebSocketOptions>(Build(MqttTransport.Wss).ChannelOptions);

        Assert.False(ws.TlsOptions!.AllowUntrustedCertificates);
        Assert.False(ws.TlsOptions.IgnoreCertificateChainErrors);
        Assert.False(ws.TlsOptions.IgnoreCertificateRevocationErrors);
        Assert.Null(ws.TlsOptions.CertificateValidationHandler);
    }

    [Fact]
    public void WSS开启自签证书后放宽全部校验()
    {
        var ws = Assert.IsType<MqttClientWebSocketOptions>(
            Build(MqttTransport.Wss, allowInsecureTls: true).ChannelOptions);

        Assert.True(ws.TlsOptions!.AllowUntrustedCertificates);
        Assert.True(ws.TlsOptions.IgnoreCertificateChainErrors);
        Assert.True(ws.TlsOptions.IgnoreCertificateRevocationErrors);
        Assert.NotNull(ws.TlsOptions.CertificateValidationHandler);
    }

    [Fact]
    public void TCP不受自签证书开关影响()
    {
        var tcp = Assert.IsType<MqttClientTcpOptions>(
            Build(MqttTransport.Tcp, allowInsecureTls: true).ChannelOptions);

        Assert.NotEqual(true, tcp.TlsOptions?.UseTls);
    }

    [Theory]
    [InlineData("/mqtt", "ws://broker.local:9001/mqtt")]
    [InlineData("/custom/path", "ws://broker.local:9001/custom/path")]
    [InlineData("custom", "ws://broker.local:9001/custom")]
    [InlineData("  ", "ws://broker.local:9001/mqtt")]
    public void WebSocket路径会规范化(string wsPath, string expectedUri)
    {
        var ws = Assert.IsType<MqttClientWebSocketOptions>(
            Build(MqttTransport.Ws, wsPath: wsPath).ChannelOptions);

        Assert.Equal(expectedUri, ws.Uri);
    }

    [Fact]
    public void WebSocketURI用配置的端口()
    {
        var ws = Assert.IsType<MqttClientWebSocketOptions>(
            Build(MqttTransport.Wss, port: 8443).ChannelOptions);

        Assert.Equal("wss://broker.local:8443/mqtt", ws.Uri);
    }

    [Fact]
    public void 自动生成的ClientId带win前缀()
    {
        var clientId = MqttOptionsFactory.NewClientId();

        Assert.StartsWith("win-", clientId, StringComparison.Ordinal);
        Assert.Equal(12, clientId.Length);
    }

    [Fact]
    public void 空ClientId会被拒绝()
    {
        Assert.Throws<ArgumentException>(() => MqttOptionsFactory.Build(new AppSettings { Host = "h" }, "  "));
    }
}
