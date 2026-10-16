using System.Net.Sockets;
using System.Security.Authentication;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;

namespace MqttNotify.Core;

/// <summary>
/// 把 <see cref="AppSettings"/> 翻译成 MQTTnet 的连接参数。
///
/// 单独抽出来是为了能用单元测试覆盖「TCP / WS / WSS × 是否信任自签证书」这个矩阵：
/// WSS 端到端联调需要真实 TLS 反代，成本高，所以这一层必须靠单测兜住。
/// </summary>
public static class MqttOptionsFactory
{
    /// <summary>Keep Alive 60s，与手机端及 push-server 文档一致。</summary>
    public static TimeSpan KeepAlive { get; } = TimeSpan.FromSeconds(60);

    /// <summary>建连超时。MQTTnet 默认 10s，显式写出来免得依赖默认值。</summary>
    public static TimeSpan ConnectTimeout { get; } = TimeSpan.FromSeconds(10);

    public static MqttClientOptions Build(AppSettings settings, string clientId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var builder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(KeepAlive)
            .WithTimeout(ConnectTimeout);

        if (settings.Transport.IsWebSocket())
        {
            // WS/WSS：路径由 AppSettings.EffectiveWsPath 规范化（必须以 / 开头）。
            // Mosquitto 自身忽略路径，但 nginx/caddy 会按 location 匹配，所以可配置。
            builder.WithWebSocketServer(o => o.WithUri(
                settings.Transport.DisplayUrl(settings.Host, settings.Port, settings.EffectiveWsPath)));
        }
        else
        {
            builder.WithTcpServer(settings.Host, settings.Port, AddressFamily.Unspecified);
        }

        if (settings.Transport.IsTls())
        {
            builder.WithTlsOptions(o =>
            {
                // SslProtocols.None = 交给操作系统协商（TLS 1.2/1.3），不要手工钉死版本
                o.UseTls(true).WithSslProtocols(SslProtocols.None);

                if (settings.AllowInsecureTls)
                {
                    // 仅内网自签证书场景：同时放宽链校验、吊销校验与主机名校验。
                    // 只放宽链校验是不够的——自签证书的 CN/SAN 通常也跟访问地址对不上。
                    o.WithAllowUntrustedCertificates(true)
                        .WithIgnoreCertificateChainErrors(true)
                        .WithIgnoreCertificateRevocationErrors(true)
                        .WithCertificateValidationHandler(_ => true);
                }
            });
        }

        return builder.Build();
    }

    /// <summary>生成默认 Client ID，与手机端同构：<c>win-xxxxxxxx</c>。</summary>
    public static string NewClientId() => "win-" + Guid.NewGuid().ToString("N")[..8];
}
