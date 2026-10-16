using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Xunit;
using Xunit.Abstractions;

namespace MqttNotify.Core.Tests;

/// <summary>
/// 连接服务的端到端测试。
///
/// MQTTnet 自带 broker，所以这里能起一个<b>进程内</b>的 broker，
/// 不需要外部 Mosquitto —— 这是 Windows 端相比手机端在可测性上的优势。
/// </summary>
public class MqttConnectionServiceIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public MqttConnectionServiceIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task 端到端_连上broker后收到下行消息会抛出提醒()
    {
        var port = GetFreeTcpPort();
        var broker = CreateBroker(port);
        await broker.StartAsync();

        try
        {
            await using var service = NewService();

            var alerts = new List<AlertRecord>();
            var connected = new TaskCompletionSource();

            service.AlertReceived += (_, alert) =>
            {
                lock (alerts)
                {
                    alerts.Add(alert);
                }
            };
            service.StatusChanged += (_, snapshot) =>
            {
                if (snapshot.Status == ConnectionStatus.Connected)
                {
                    connected.TrySetResult();
                }
            };

            await service.ApplySettingsAsync(Settings(port));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(15));

            await broker.InjectApplicationMessage(
                "taskops/todo/7",
                "【待办提醒】交电费\n主卧电表，户号123456",
                MqttQualityOfServiceLevel.AtLeastOnce,
                false);

            var alert = await WaitForAlertAsync(alerts, TimeSpan.FromSeconds(15));

            Assert.Equal(7, alert.TaskId);
            Assert.Equal("交电费", alert.Title);
            Assert.Equal("主卧电表，户号123456", alert.Body);
            Assert.Equal("taskops/todo/7", alert.Topic);
        }
        finally
        {
            await broker.StopAsync();
        }
    }

    [Fact]
    public async Task 主题不在订阅范围内时不会收到提醒()
    {
        var port = GetFreeTcpPort();
        var broker = CreateBroker(port);
        await broker.StartAsync();

        try
        {
            await using var service = NewService();

            var alerts = new List<AlertRecord>();
            var connected = new TaskCompletionSource();

            service.AlertReceived += (_, alert) =>
            {
                lock (alerts)
                {
                    alerts.Add(alert);
                }
            };
            service.StatusChanged += (_, snapshot) =>
            {
                if (snapshot.Status == ConnectionStatus.Connected)
                {
                    connected.TrySetResult();
                }
            };

            await service.ApplySettingsAsync(Settings(port));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // 订阅的是 taskops/todo/#，这条不该命中
            await broker.InjectApplicationMessage(
                "other/topic/1",
                "【待办提醒】不该收到",
                MqttQualityOfServiceLevel.AtLeastOnce,
                false);

            await Task.Delay(TimeSpan.FromSeconds(3));

            lock (alerts)
            {
                Assert.Empty(alerts);
            }
        }
        finally
        {
            await broker.StopAsync();
        }
    }

    [Fact]
    public async Task broker不可达时进入失败态并持续重试()
    {
        // 故意不启动 broker
        var port = GetFreeTcpPort();

        await using var service = NewService();
        var failures = 0;
        service.StatusChanged += (_, snapshot) =>
        {
            if (snapshot.Status == ConnectionStatus.Failed)
            {
                Interlocked.Increment(ref failures);
            }
        };

        await service.ApplySettingsAsync(Settings(port));

        // 退避 50ms→100ms→200ms 封顶，2 秒内应发生多次重试
        await Task.Delay(TimeSpan.FromSeconds(2));

        var count = Volatile.Read(ref failures);
        _output.WriteLine($"2 秒内失败重试 {count} 次");
        Assert.True(count >= 2, $"预期持续重试，实际只失败 {count} 次");
        Assert.Equal(ConnectionStatus.Failed, service.Status.Status);
    }

    [Fact]
    public async Task broker重启后能自动重连()
    {
        var port = GetFreeTcpPort();
        var broker = CreateBroker(port);
        await broker.StartAsync();

        await using var service = NewService();
        var connectedCount = 0;
        var reconnected = new TaskCompletionSource();

        service.StatusChanged += (_, snapshot) =>
        {
            if (snapshot.Status == ConnectionStatus.Connected &&
                Interlocked.Increment(ref connectedCount) >= 2)
            {
                reconnected.TrySetResult();
            }
        };

        await service.ApplySettingsAsync(Settings(port));
        await WaitForAsync(() => Volatile.Read(ref connectedCount) >= 1, TimeSpan.FromSeconds(15));

        // 拔掉 broker，再原地重启一个
        await broker.StopAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var restarted = CreateBroker(port);
        await restarted.StartAsync();

        try
        {
            await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(Volatile.Read(ref connectedCount) >= 2);
        }
        finally
        {
            await restarted.StopAsync();
        }
    }

    [Fact]
    public async Task 掉线后状态不再停留在已连接()
    {
        var port = GetFreeTcpPort();
        var broker = CreateBroker(port);
        await broker.StartAsync();

        await using var service = NewService();
        var connected = new TaskCompletionSource();
        var reconnecting = new TaskCompletionSource();

        service.StatusChanged += (_, snapshot) =>
        {
            if (snapshot.Status == ConnectionStatus.Connected)
            {
                connected.TrySetResult();
            }

            if (snapshot.Status == ConnectionStatus.Reconnecting)
            {
                reconnecting.TrySetResult();
            }
        };

        await service.ApplySettingsAsync(Settings(port));
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await broker.StopAsync();

        // 掉线后必须立刻切走：中间隔着退避等待，若等到下一轮循环才更新，
        // 界面会在整个重连等待期间一直显示「已连接」。
        await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotEqual(ConnectionStatus.Connected, service.Status.Status);
    }

    [Fact]
    public async Task 未配置broker地址时直接失败()
    {
        await using var service = NewService();

        await service.ApplySettingsAsync(new AppSettings { Host = string.Empty });

        Assert.Equal(ConnectionStatus.Failed, service.Status.Status);
        Assert.False(string.IsNullOrWhiteSpace(service.Status.Detail));
    }

    [Fact]
    public async Task 停止后状态回到Stopped()
    {
        await using var service = NewService();

        await service.ApplySettingsAsync(Settings(GetFreeTcpPort()));
        await service.StopAsync();

        Assert.Equal(ConnectionStatus.Stopped, service.Status.Status);
    }

    // ---------------------------------------------------------------------
    // 测试脚手架
    // ---------------------------------------------------------------------

    private static MqttConnectionService NewService() => new(
        initialBackoff: TimeSpan.FromMilliseconds(50),
        maxBackoff: TimeSpan.FromMilliseconds(200));

    private static AppSettings Settings(int port) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        ClientId = "test-" + Guid.NewGuid().ToString("N")[..8],
        Transport = MqttTransport.Tcp,
        TopicBase = "taskops/todo",
    };

    private static MqttServer CreateBroker(int port)
    {
        var factory = new MqttFactory();
        var options = factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointPort(port)
            .WithPersistentSessions(false)
            .Build();

        return factory.CreateMqttServer(options);
    }

    /// <summary>占一个临时端口再释放，交给 broker 用。测试环境下足够可靠。</summary>
    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<AlertRecord> WaitForAlertAsync(List<AlertRecord> alerts, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (alerts)
            {
                if (alerts.Count > 0)
                {
                    return alerts[0];
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("未在超时时间内收到提醒");
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("条件未在超时时间内满足");
    }
}
