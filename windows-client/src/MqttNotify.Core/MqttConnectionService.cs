using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace MqttNotify.Core;

/// <summary>
/// 常驻 MQTT 连接服务。
///
/// 职责：
/// <list type="number">
///   <item>按 <see cref="AppSettings"/> 建立 MQTT 3.1.1 连接并订阅 <c>{base}/#</c>（QoS 1、clean session、KeepAlive 60s）；</item>
///   <item>收到消息即解析并通过 <see cref="AlertReceived"/> 抛出，由界面层决定怎么提醒；</item>
///   <item>断线自动重连：首次连接失败与中途断开都走本地指数退避（2s→60s 封顶）。</item>
/// </list>
///
/// 与手机端的差异：HiveMQ 自带 <c>automaticReconnect</c>，而 MQTTnet 没有，
/// 所以这里的「等断开 → 退避 → 重连」循环是自己实现的。
///
/// 线程模型：所有状态变更都可能在后台线程发生，订阅方（WPF）需自行切回 UI 线程。
/// </summary>
public sealed class MqttConnectionService : IAsyncDisposable
{
    /// <summary>首连失败 / 断线后的初始退避。</summary>
    public static TimeSpan DefaultInitialBackoff { get; } = TimeSpan.FromSeconds(2);

    /// <summary>退避上限。</summary>
    public static TimeSpan DefaultMaxBackoff { get; } = TimeSpan.FromSeconds(60);

    /// <summary>payload 无「【待办提醒】」前缀时使用的兜底标题。</summary>
    public const string DefaultAlertTitle = "新提醒";

    /// <summary>在线状态心跳间隔；服务端超过 180 秒收不到心跳就视为离线。</summary>
    public static TimeSpan PresenceHeartbeatInterval { get; } = TimeSpan.FromSeconds(60);

    private readonly Func<IMqttClient> _clientFactory;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly string _fallbackTitle;

    /// <summary>串行化「应用配置 / 停止」，避免并发重建连接。</summary>
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    private readonly object _stateLock = new();

    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int _generation;
    private ConnectionSnapshot _status = ConnectionSnapshot.Stopped;

    public MqttConnectionService(
        Func<IMqttClient>? clientFactory = null,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null,
        string fallbackTitle = DefaultAlertTitle)
    {
        _clientFactory = clientFactory ?? (() => new MqttFactory().CreateMqttClient());
        _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
        _maxBackoff = maxBackoff ?? DefaultMaxBackoff;
        _fallbackTitle = fallbackTitle;
    }

    /// <summary>连接状态变化（后台线程触发）。</summary>
    public event EventHandler<ConnectionSnapshot>? StatusChanged;

    /// <summary>收到一条提醒（后台线程触发）。</summary>
    public event EventHandler<AlertRecord>? AlertReceived;

    public ConnectionSnapshot Status
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    /// <summary>
    /// 应用配置并（重新）连接：先拆掉旧连接，再按新配置起一条新链路。
    /// 配置不合法时置为 <see cref="ConnectionStatus.Failed"/> 并直接返回。
    /// </summary>
    public async Task ApplySettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _applyLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);

            if (!settings.IsValid)
            {
                Publish(ConnectionSnapshot.Failed("尚未配置 Broker 地址"));
                return;
            }

            var generation = Volatile.Read(ref _generation);
            var cts = new CancellationTokenSource();
            _runCts = cts;
            _runTask = Task.Run(() => RunAsync(settings, generation, cts.Token), CancellationToken.None);
        }
        finally
        {
            _applyLock.Release();
        }
    }

    /// <summary>停止连接并置为 <see cref="ConnectionStatus.Stopped"/>。</summary>
    public async Task StopAsync()
    {
        await _applyLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            Publish(ConnectionSnapshot.Stopped);
        }
        finally
        {
            _applyLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _applyLock.Dispose();
    }

    // ---------------------------------------------------------------------
    // 连接循环
    // ---------------------------------------------------------------------

    private async Task RunAsync(AppSettings settings, int generation, CancellationToken cancellationToken)
    {
        var endpoint = settings.Transport.DisplayUrl(
            settings.Host,
            settings.Port,
            settings.EffectiveWsPath);

        var clientId = settings.EffectiveClientId;

        var options = MqttOptionsFactory.Build(settings, clientId);
        var backoff = _initialBackoff;

        // 区分「初次连接」与「掉线重连」：两者对用户的含义不同，状态文案也该不同
        var everConnected = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var client = _clientFactory();

            // 每次循环一个信号量：断开（或连上后立刻掉线）都会置位，循环据此进入重连
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            client.DisconnectedAsync += _ =>
            {
                disconnected.TrySetResult();
                return Task.CompletedTask;
            };

            client.ApplicationMessageReceivedAsync += e =>
            {
                HandleMessage(e);
                return Task.CompletedTask;
            };

            try
            {
                // 只有首轮显示「连接中」；掉线后的重试保持「重连中」，不再回退成「连接中」
                if (!everConnected)
                {
                    Publish(ConnectionSnapshot.Connecting(endpoint), generation);
                }

                await client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);

                var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f
                        .WithTopic(settings.SubscribeTopic)
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .Build();

                await client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);

                // 连上即复位退避：下一次断线只需等最短时间，不必从上次的峰值重来
                everConnected = true;
                backoff = _initialBackoff;
                Publish(
                    ConnectionSnapshot.Connected($"{endpoint} · {settings.SubscribeTopic}"),
                    generation);

                try
                {
                    await PublishPresenceAsync(client, clientId, settings.SubscribeTopic, online: true, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // 在线状态只是管理页辅助信息，发布失败不应影响业务订阅。
                }

                // 连接期间定时刷新 last_seen；异常断线时 broker 会代发 Will（online=false）。
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeat = PresenceHeartbeatAsync(client, clientId, settings.SubscribeTopic, heartbeatCts.Token);
                try
                {
                    await disconnected.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await heartbeatCts.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        await heartbeat.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // 掉线要「立刻」置为重连中：后面还隔着退避等待，
                // 若等下一轮循环再改，界面会在等待期间一直显示「已连接」。
                Publish(ConnectionSnapshot.Reconnecting("连接断开，正在重连…"), generation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // 从未连上过 = 地址/网络有问题（「连接失败」）；
                // 连上过再掉 = 「重连中」，附带本次失败原因
                Publish(
                    everConnected
                        ? ConnectionSnapshot.Reconnecting($"重连中：{Describe(ex)}")
                        : ConnectionSnapshot.Failed(Describe(ex)),
                    generation);
            }
            finally
            {
                // 正常断开（用户停止 / 切换配置）时主动落一次离线状态；异常断线走 Will。
                if (client.IsConnected)
                {
                    try
                    {
                        await PublishPresenceAsync(client, clientId, settings.SubscribeTopic, online: false, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // 离线状态发布失败不阻断断开流程，服务端还有 TTL 兜底。
                    }
                }

                await SafeDisconnectAsync(client).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = Min(backoff * 2, _maxBackoff);
        }
    }

    private void HandleMessage(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.PayloadSegment.ToArray();
        var parsed = MqttMessageParser.Parse(topic, payload, _fallbackTitle);
        var alert = new AlertRecord(
            parsed.Kind,
            parsed.TaskId,
            parsed.ServerName,
            parsed.ServiceName,
            parsed.Status,
            topic,
            parsed.Title,
            parsed.Body,
            DateTimeOffset.Now);

        AlertReceived?.Invoke(this, alert);
    }

    private static async Task PresenceHeartbeatAsync(
        IMqttClient client,
        string clientId,
        string topic,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(PresenceHeartbeatInterval, cancellationToken).ConfigureAwait(false);

            if (!client.IsConnected)
            {
                continue;
            }

            try
            {
                // 心跳走 QoS0 + 非 retained：少一次 PUBACK，也避免反复写 retained。
                // 首次上线和离线 Will 仍用 QoS1 + retained。
                await PublishPresenceAsync(
                    client,
                    clientId,
                    topic,
                    online: true,
                    cancellationToken,
                    qos: MqttQualityOfServiceLevel.AtMostOnce,
                    retain: false).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // 单次心跳失败不影响连接，下一次继续。
            }
        }
    }

    private static async Task PublishPresenceAsync(
        IMqttClient client,
        string clientId,
        string topic,
        bool online,
        CancellationToken cancellationToken,
        MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce,
        bool retain = true)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(MqttPresence.TopicFor(clientId))
            .WithPayload(MqttPresence.Payload(clientId, topic, online))
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(retain)
            .Build();

        await client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SafeDisconnectAsync(IMqttClient client)
    {
        try
        {
            if (client.IsConnected)
            {
                var disconnectOptions = new MqttClientDisconnectOptionsBuilder().Build();
                await client.DisconnectAsync(disconnectOptions, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 断开失败不影响后续：这个 client 实例不再复用
        }

        client.Dispose();
    }

    private async Task StopCoreAsync()
    {
        // 递增代号：让在途 run 的迟到状态更新失效（等价于手机端的 generation 机制）
        Interlocked.Increment(ref _generation);

        var cts = _runCts;
        var task = _runTask;
        _runCts = null;
        _runTask = null;

        if (cts is null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);

            if (task is not null)
            {
                // 给个上限，避免 Dispose / 关闭窗口时卡死
                await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // TimeoutException / 取消导致的异常，都不该阻断停止流程
        }
        finally
        {
            cts.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // 状态广播
    // ---------------------------------------------------------------------

    /// <summary>无条件发布状态（仅用于控制路径：停止、配置非法）。</summary>
    private void Publish(ConnectionSnapshot snapshot)
    {
        lock (_stateLock)
        {
            _status = snapshot;
        }

        StatusChanged?.Invoke(this, snapshot);
    }

    /// <summary>带代号校验地发布状态，防止旧连接的迟到回调覆盖当前状态。</summary>
    private void Publish(ConnectionSnapshot snapshot, int generation)
    {
        if (Volatile.Read(ref _generation) != generation)
        {
            return;
        }

        Publish(snapshot);
    }

    private static string Describe(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
