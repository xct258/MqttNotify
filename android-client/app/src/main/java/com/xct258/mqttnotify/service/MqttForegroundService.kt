package com.xct258.mqttnotify.service

import android.app.Notification
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.net.ConnectivityManager
import android.net.Network
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import android.os.SystemClock
import android.util.Log
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.hivemq.client.mqtt.MqttClient
import com.hivemq.client.mqtt.MqttClientSslConfig
import com.hivemq.client.mqtt.MqttWebSocketConfig
import com.hivemq.client.mqtt.datatypes.MqttQos
import com.hivemq.client.mqtt.lifecycle.MqttDisconnectSource
import com.hivemq.client.mqtt.mqtt3.Mqtt3AsyncClient
import com.hivemq.client.mqtt.mqtt3.message.publish.Mqtt3Publish
import com.xct258.mqttnotify.R
import com.xct258.mqttnotify.data.AppSettings
import com.xct258.mqttnotify.data.SettingsRepository
import com.xct258.mqttnotify.mqtt.AlertStore
import com.xct258.mqttnotify.mqtt.InsecureSsl
import com.xct258.mqttnotify.mqtt.MqttMessageParser
import com.xct258.mqttnotify.mqtt.MqttPresence
import com.xct258.mqttnotify.mqtt.MqttState
import com.xct258.mqttnotify.mqtt.SubscriptionStore
import com.xct258.mqttnotify.notify.NotificationHelper
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit
import javax.net.ssl.HostnameVerifier

/**
 * 常驻 MQTT 前台服务。
 *
 * 职责：
 *  1. 以前台服务形式保活，读取 [SettingsRepository] 中的配置；
 *  2. 建立 MQTT 3.1.1 持久会话并订阅 `{base}/#`（QoS 1、KeepAlive 60s）；
 *  3. 收到消息即解析、记录到 [MqttState] 并弹出系统通知；
 *  4. 断线由 HiveMQ 自动重连（指数退避），首次连接失败则本地退避重试；
 *  5. 网络切换立即重连、心跳失败/链路假死主动重连，配合 [MqttWatchdog] 兜底。
 *
 * 使用持久会话（cleanSession=false）的原因：设备断网、Doze 或进程被杀期间，
 * broker 会把 QoS1 消息排队；客户端重连后立即补收，避免「离线那几分钟」漏消息。
 * 代价是 broker 会保留订阅，所以换主题时必须先取消旧主题（见 [SubscriptionStore]）。
 *
 * 配置变更后无需外部通知：服务持续 collect 设置流，变化即重连。
 */
class MqttForegroundService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    @Volatile
    private var client: Mqtt3AsyncClient? = null

    private var settingsJob: Job? = null

    /** 当前连接使用的 client id / 心跳任务，用于离线状态上报。 */
    @Volatile
    private var activeClientId: String? = null
    @Volatile
    private var activeTopic: String? = null
    private var presenceJob: Job? = null
    private var persistJob: Job? = null

    /** 网络恢复 / 连接失败退避期间，用来提前唤醒重试循环。 */
    private val retrySignal = Channel<Unit>(Channel.CONFLATED)

    /**
     * 主动重连信号。与设置流 combine 后交给 collectLatest：
     * collectLatest 会先取消并等待上一轮连接清理完成，再启动新一轮，
     * 避免旧连接的迟到回调把新连接拆掉。
     */
    private val reconnectSignal = MutableStateFlow(0L)

    private var networkCallback: ConnectivityManager.NetworkCallback? = null
    private var currentNetwork: Network? = null
    private var wakeupReceiver: BroadcastReceiver? = null

    /** 建连阶段短暂持有，避免设备刚被闹钟唤醒又立刻睡回去。 */
    private var connectWakeLock: PowerManager.WakeLock? = null

    @Volatile
    private var lastNetworkReconnectAt: Long = 0L

    /** 最近一次成功发布在线状态的时间，用于合并「收到消息 / 亮屏」触发的即时上报。 */
    @Volatile
    private var lastPresenceAt: Long = 0L

    /**
     * 连接代号：每次拆连接 / 新建立都自增。
     * 监听回调只在代号仍然“当前”时才更新状态，避免旧连接的迟到回调污染 UI。
     */
    @Volatile
    private var generation: Int = 0

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        NotificationHelper.createChannels(this)
        startForegroundCompat(serviceNotification(MqttState.Status.STOPPED, ""))
        // 用户点了「启动接收」时 start() 已把开关置 true；这里只补排闹钟，
        // 覆盖 START_STICKY 系统重启等场景。不要在 onCreate 里强制置 true，
        // 否则用户点「停止接收」后若系统又拉起服务，会被意外重新启用。
        if (MqttWatchdog.isEnabled(this)) {
            MqttWatchdog.schedule(this)
        }
        observeStatus()
        registerNetworkCallback()
        registerWakeupReceiver()
        startLivenessWatchdog()
        observeSettings()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                // 用户明确停止：关闭看门狗，避免它过一会儿又把服务拉起来。
                MqttWatchdog.setEnabled(this, false)
                stopSelf()
                return START_NOT_STICKY
            }

            ACTION_START -> MqttWatchdog.setEnabled(this, true)

            ACTION_RECONNECT -> forceReconnect("外部请求重连")

            MqttWatchdog.ACTION_WATCHDOG -> {
                if (!MqttWatchdog.isEnabled(this)) {
                    stopSelf()
                    return START_NOT_STICKY
                }
                val snapshot = MqttState.status.value
                // 进程活着但「已连接」状态已经假死时，看门狗主动踢一脚；
                // 正在连接/重连时不要打断现有退避循环。
                if (snapshot.status == MqttState.Status.CONNECTED &&
                    !MqttState.isConnectionAlive()
                ) {
                    forceReconnect("看门狗：连接假死")
                }
            }
        }

        // 重复 start 时只刷新前台通知（满足 startForeground 时限）。
        // 设置监听只在 onCreate 启动一次；这里重复 observeSettings 会取消正在
        // 连接/已连接的协程，导致「一启动就重连」。
        val snapshot = MqttState.status.value
        startForegroundCompat(serviceNotification(snapshot.status, snapshot.detail))
        return START_STICKY
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        super.onTaskRemoved(rootIntent)
        // 部分系统会把「从最近任务划掉」当成停止 App；这里主动续上前台服务。
        // 用户手动点「停止接收」走 ACTION_STOP，不会触发这里。
        Log.i(TAG, "任务被移除，保持 MQTT 前台服务")
        if (MqttWatchdog.isEnabled(this)) {
            MqttWatchdog.schedule(this)
            runCatching {
                val restart = Intent(applicationContext, MqttForegroundService::class.java)
                    .setAction(ACTION_START)
                ContextCompat.startForegroundService(applicationContext, restart)
            }.onFailure { Log.w(TAG, "任务移除后重启服务失败", it) }
        }
    }

    override fun onDestroy() {
        scope.cancel()
        unregisterNetworkCallback()
        unregisterWakeupReceiver()
        runBlocking { withTimeoutOrNull(1_000L) { teardownClient(announceOffline = true) } }
        releaseConnectWakeLock()
        MqttState.setStatus(MqttState.Status.STOPPED, "")
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        super.onDestroy()
    }

    // ---------------------------------------------------------------------
    // 设置监听 & 连接管理
    // ---------------------------------------------------------------------

    private fun observeSettings() {
        settingsJob?.cancel()
        settingsJob = scope.launch {
            combine(
                SettingsRepository.get(this@MqttForegroundService).settings
                    .map { it.normalized() }
                    .distinctUntilChanged(),
                reconnectSignal,
            ) { settings, _ -> settings }
                .collectLatest { settings -> connectWithRetry(settings) }
        }
    }

    /**
     * 惰性刷新设置再连接：
     * 先拆掉旧连接，未配置则提示后退出；已配置则循环重试直到连上为止
     * （连上后交给 HiveMQ 的自动重连接管）。
     *
     * 退避等待期间如果网络恢复（[retrySignal]），立即醒来重试，不再死等 60s。
     */
    private suspend fun connectWithRetry(settings: AppSettings) {
        teardownClient()
        drainRetrySignals()

        if (!settings.isValid) {
            MqttState.setStatus(MqttState.Status.FAILED, "尚未配置 Broker 地址")
            return
        }

        var backoffMs = 2_000L
        try {
            while (currentCoroutineContext().isActive) {
                acquireConnectWakeLock()
                try {
                    establish(settings)
                    drainRetrySignals()
                    return
                } catch (e: CancellationException) {
                    // collectLatest 取消（配置变更/主动重连）时，也要把半成品连接拆干净
                    teardownClient()
                    throw e
                } catch (e: Exception) {
                    Log.w(TAG, "连接失败，${backoffMs}ms 后重试", e)
                    teardownClient()
                    MqttState.setStatus(MqttState.Status.FAILED, e.message ?: e.javaClass.simpleName)
                    // 网络恢复信号会提前结束这次等待；没有信号则按指数退避继续。
                    withTimeoutOrNull(backoffMs) { retrySignal.receive() }
                    backoffMs = (backoffMs * 2).coerceAtMost(MAX_BACKOFF_MS)
                }
            }
        } finally {
            releaseConnectWakeLock()
        }
    }

    private fun drainRetrySignals() {
        while (retrySignal.tryReceive().isSuccess) {
            // 清掉连接建立前积压的旧信号，避免刚连上就被“提前重试”。
        }
    }

    /**
     * 请求按最新配置重连。
     *
     * 网络切换、心跳失败、看门狗发现假死时都走这里。只递增重连信号，
     * 由 collectLatest 取消并等待上一轮清理完成后再连，避免出现两条连接
     * 同时跑、旧连接的清理代码误伤新连接的竞态。
     */
    private fun forceReconnect(reason: String) {
        Log.i(TAG, "主动重连：$reason")
        MqttState.setStatus(MqttState.Status.RECONNECTING, reason)
        reconnectSignal.update { it + 1L }
    }

    private fun acquireConnectWakeLock() {
        val powerManager = getSystemService(PowerManager::class.java) ?: return
        val wakeLock = connectWakeLock ?: powerManager.newWakeLock(
            PowerManager.PARTIAL_WAKE_LOCK,
            "$packageName:mqtt-connect",
        ).apply {
            setReferenceCounted(false)
            connectWakeLock = this
        }
        if (!wakeLock.isHeld) {
            runCatching { wakeLock.acquire(CONNECT_WAKE_LOCK_TIMEOUT_MS) }
                .onFailure { Log.w(TAG, "获取建连 WakeLock 失败", it) }
        }
    }

    private fun releaseConnectWakeLock() {
        val wakeLock = connectWakeLock ?: return
        runCatching {
            if (wakeLock.isHeld) wakeLock.release()
        }.onFailure { Log.w(TAG, "释放建连 WakeLock 失败", it) }
    }

    private suspend fun establish(settings: AppSettings) {
        val gen = generation
        val clientId = settings.effectiveClientId
        val subscribeTopic = settings.subscribeTopic
        activeClientId = clientId
        activeTopic = subscribeTopic

        // 展示用地址（同时覆盖 TCP/WS/WSS），状态栏与日志都用它，便于排查
        val endpoint = settings.transport.displayUrl(
            settings.host,
            settings.port,
            settings.effectiveWsPath,
        )

        MqttState.setStatus(MqttState.Status.CONNECTING, endpoint)

        val builder = MqttClient.builder()
            .useMqttVersion3()
            .identifier(clientId)
            .serverHost(settings.host)
            .serverPort(settings.port)
            // 不放 MQTT Will：
            // Will 的 payload 在连接建立时就固定了（ts 是连接开始时间），重连后旧连接的
            // Will 可能迟到，把刚上线的状态覆盖成离线，管理页就会出现「能收消息但显示离线」。
            // 异常断线改由 retained 心跳 + 服务端 TTL（默认 180s）兜底；
            // 用户主动停止时仍会 publish 一条实时的 online=false。
            .automaticReconnectWithDefaultConfig()
            .addConnectedListener {
                if (gen == generation) {
                    MqttState.setStatus(MqttState.Status.CONNECTED, endpoint)
                    MqttState.markActivity()
                    // HiveMQ 自动重连不会重新执行 establish。这里强制补一次在线状态，
                    // 否则 broker 上的 retained 可能还是断线时的 online=false。
                    publishPresenceNow("MQTT 连接成功/重连成功", force = true)
                }
            }
            .addDisconnectedListener { ctx ->
                if (gen == generation) {
                    // getCause() 为 @NotNull；用 source 区分主动断开(USER)与意外断开
                    val byUser = ctx.source == MqttDisconnectSource.USER
                    Log.w(TAG, "连接断开（source=${ctx.source}）", ctx.cause)
                    MqttState.setStatus(
                        if (byUser) MqttState.Status.STOPPED else MqttState.Status.RECONNECTING,
                        if (byUser) "连接已断开" else "连接断开，正在重连…",
                    )
                }
            }

        if (settings.transport.isWebSocket) {
            // WS/WSS：必须指定 serverPath。Mosquitto 自身忽略路径，但 nginx/caddy 会按
            // location 匹配，故默认 /mqtt 且允许自定义。
            builder.webSocketConfig(
                MqttWebSocketConfig.builder()
                    .serverPath(settings.effectiveWsPath)
                    .build()
            )
        }

        if (settings.transport.isTls) {
            // WSS = WebSocket + TLS：默认走系统信任库；自签证书可显式跳过校验
            val sslBuilder = MqttClientSslConfig.builder()
            if (settings.allowInsecureTls) {
                sslBuilder.trustManagerFactory(InsecureSsl.trustManagerFactory())
                // 自签证书的 CN/SAN 常与访问地址对不上，跳过校验时一并放宽主机名校验
                sslBuilder.hostnameVerifier(HostnameVerifier { _, _ -> true })
            }
            builder.sslConfig(sslBuilder.build())
        }

        val newClient = builder.buildAsync()

        client = newClient

        newClient.connectWith()
            // 持久会话：断网/Doze/进程被杀期间，broker 会把 QoS1 消息排队，
            // 重连后立即补收。client id 安装期固定，所以会话可以跨进程恢复。
            .cleanSession(false)
            .keepAlive(KEEP_ALIVE_SECONDS)
            .send()
            .await()

        // 持久会话会保留旧订阅。用户改过主题时先退订旧主题，避免新旧主题重复收信。
        val previousTopic = SubscriptionStore.lastTopic(this)
        if (!previousTopic.isNullOrBlank() && previousTopic != subscribeTopic) {
            runCatching {
                newClient.unsubscribeWith()
                    .topicFilter(previousTopic)
                    .send()
                    .await()
            }.onFailure { Log.w(TAG, "取消旧订阅 $previousTopic 失败", it) }
        }

        newClient.subscribeWith()
            .topicFilter(subscribeTopic)
            .qos(MqttQos.AT_LEAST_ONCE)
            .callback { publish -> handlePublish(publish) }
            .send()
            .await()
        SubscriptionStore.setLastTopic(this, subscribeTopic)
        MqttState.markActivity()

        // 上线/心跳都使用 QoS1 + retained：
        //  - QoS0 在弱网、休眠、broker 忙时可能静默丢包，管理页会误判离线；
        //  - retained 保证 push-server 重启后一订阅就能看到在线，而不是读到旧的 offline。
        runCatching {
            publishPresence(
                newClient,
                clientId,
                subscribeTopic,
                online = true,
                qos = MqttQos.AT_LEAST_ONCE,
                retain = true,
                timeoutMs = PRESENCE_PUBLISH_TIMEOUT_MS,
            )
        }.onSuccess {
            notePresenceSent()
            MqttState.markActivity()
        }.onFailure { Log.w(TAG, "上线状态发布失败", it) }

        presenceJob?.cancel()
        presenceJob = scope.launch {
            while (isActive) {
                delay(PRESENCE_HEARTBEAT_MS)
                val current = client ?: break
                val ok = runCatching {
                    publishPresence(
                        current,
                        clientId,
                        subscribeTopic,
                        online = true,
                        qos = MqttQos.AT_LEAST_ONCE,
                        retain = true,
                        timeoutMs = PRESENCE_PUBLISH_TIMEOUT_MS,
                    )
                }.isSuccess
                if (ok) {
                    notePresenceSent()
                    MqttState.markActivity()
                } else {
                    // 单次心跳失败不要让循环退出：HiveMQ 会自动重连，
                    // 重连后的 ConnectedListener 会立刻补发 retained 在线状态。
                    // 真正的断线由 DisconnectedListener / 链路假死检测兜底。
                    Log.w(TAG, "在线心跳失败，等待下次心跳或自动重连")
                }
            }
        }

        MqttState.setStatus(
            MqttState.Status.CONNECTED,
            "$endpoint · ${settings.subscribeTopic}",
        )
        Log.i(TAG, "已连接 $endpoint，订阅 ${settings.subscribeTopic}（持久会话）")
    }

    private suspend fun publishPresence(
        client: Mqtt3AsyncClient,
        clientId: String,
        topic: String,
        online: Boolean,
        qos: MqttQos = MqttQos.AT_LEAST_ONCE,
        retain: Boolean = true,
        timeoutMs: Long? = null,
    ) {
        val future = client.publishWith()
            .topic(MqttPresence.topicFor(clientId))
            .payload(MqttPresence.payload(this, clientId, topic, online))
            .qos(qos)
            .retain(retain)
            .send()
        if (timeoutMs == null) future.await() else future.await(timeoutMs)
    }

    private fun notePresenceSent() {
        lastPresenceAt = SystemClock.elapsedRealtime()
    }

    /**
     * 由「收到消息 / 亮屏解锁 / MQTT 重连」触发的即时在线上报。
     *
     * 这些事件本身已经证明客户端是活的，顺便刷新一次 presence，管理页就不会
     * 出现「能收消息但显示离线」的窗口。20 秒内只发一次，避免补收消息时刷屏。
     */
    private fun publishPresenceNow(reason: String, force: Boolean = false) {
        val now = SystemClock.elapsedRealtime()
        if (!force && now - lastPresenceAt < PRESENCE_MIN_INTERVAL_MS) return

        val current = client ?: return
        val clientId = activeClientId ?: return
        val topic = activeTopic ?: return
        lastPresenceAt = now // 先占位，合并并发触发

        scope.launch {
            runCatching {
                publishPresence(
                    current,
                    clientId,
                    topic,
                    online = true,
                    qos = MqttQos.AT_LEAST_ONCE,
                    retain = true,
                    timeoutMs = PRESENCE_PUBLISH_TIMEOUT_MS,
                )
            }.onSuccess {
                MqttState.markActivity()
            }.onFailure {
                Log.w(TAG, "即时在线状态上报失败（$reason）", it)
            }
        }
    }

    /**
     * 拆掉当前连接。
     *
     * @param announceOffline 只有用户主动停止 / 服务销毁时才上报离线。
     *   普通重连不要发 offline，否则每次网络切换都会让管理页闪一下离线。
     */
    private suspend fun teardownClient(announceOffline: Boolean = false) {
        generation++
        presenceJob?.cancel()
        presenceJob = null

        val old = client ?: return
        val clientId = activeClientId
        val topic = activeTopic
        client = null
        activeClientId = null
        activeTopic = null

        // 主动重连时当前协程可能已经被取消；拆连接属于清理动作，必须继续执行。
        withContext(Dispatchers.IO + NonCancellable) {
            if (announceOffline && clientId != null && topic != null) {
                runCatching {
                    // 断网时 publish 可能一直等不到 ack，必须设上限，别卡死 onDestroy
                    publishPresence(
                        old,
                        clientId,
                        topic,
                        online = false,
                        timeoutMs = 1_000L,
                    )
                }
            }
            runCatching { old.disconnect().get(1, TimeUnit.SECONDS) }
        }
    }

    private fun handlePublish(publish: Mqtt3Publish) {
        MqttState.markActivity()
        // 能收到消息说明链路一定活着，立即把在线状态刷新给管理页，
        // 不必等下一次 60 秒心跳（休眠/ROM 冻结时心跳可能被延后）。
        publishPresenceNow("收到消息")
        val topic = publish.topic.toString()
        val parsed = MqttMessageParser.parse(
            topic,
            publish.payloadAsBytes,
            getString(R.string.default_alert_title),
        )
        val alert = MqttState.addAlert(parsed.taskId, topic, parsed.title, parsed.body)
        Log.i(TAG, "收到提醒 [$topic] ${parsed.title}")
        // 通知直接发：binder 调用很快，而且不能因为服务恰好被销毁/协程被取消
        // 而把已经收到的提醒丢掉。落盘较重，仍然异步且合并写。
        runCatching {
            NotificationHelper.notifyAlert(this@MqttForegroundService, alert)
        }.onFailure { Log.w(TAG, "发送提醒通知失败", it) }
        schedulePersist()
    }

    /**
     * 持久会话补收时可能一瞬间到达几十条消息；合并写盘，避免每条都重写整个
     * alerts.json。写盘任务读取的是执行时的最新列表，并发下也不会写回旧数据。
     */
    private fun schedulePersist() {
        persistJob?.cancel()
        persistJob = scope.launch(Dispatchers.IO) {
            delay(PERSIST_DEBOUNCE_MS)
            AlertStore.save(this@MqttForegroundService, MqttState.alerts.value)
        }
    }

    // ---------------------------------------------------------------------
    // 网络监听 & 链路自愈
    // ---------------------------------------------------------------------

    /**
     * 监听默认网络变化。Wi-Fi/移动数据切换后，旧 TCP/WebSocket 往往已经失效，
     * 但 KeepAlive 要等到下一个周期才会发现；这里立即重连，把空窗期压到秒级。
     */
    private fun registerNetworkCallback() {
        val connectivityManager = getSystemService(ConnectivityManager::class.java) ?: return
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                val previous = currentNetwork
                currentNetwork = network
                val switched = previous != null && previous != network
                Log.i(TAG, "默认网络可用：$network（切换=$switched）")
                scope.launch { handleNetworkAvailable(switched) }
            }

            override fun onLost(network: Network) {
                // 不要在这里直接把状态改成「重连中」：Wi-Fi 切移动数据时，
                // 新网络的 onAvailable 可能已经先到，onLost 后到会覆盖掉正常状态。
                // 真正的断线交给 MQTT 的 DisconnectedListener / 心跳假死检测。
                Log.w(TAG, "默认网络断开：$network")
            }
        }

        runCatching { connectivityManager.registerDefaultNetworkCallback(callback) }
            .onSuccess { networkCallback = callback }
            .onFailure { Log.w(TAG, "注册网络回调失败", it) }
    }

    private fun unregisterNetworkCallback() {
        val callback = networkCallback ?: return
        networkCallback = null
        runCatching {
            getSystemService(ConnectivityManager::class.java)?.unregisterNetworkCallback(callback)
        }.onFailure { Log.w(TAG, "注销网络回调失败", it) }
    }

    private fun handleNetworkAvailable(switched: Boolean) {
        val now = SystemClock.elapsedRealtime()
        if (now - lastNetworkReconnectAt < NETWORK_DEBOUNCE_MS) return
        lastNetworkReconnectAt = now

        val connected = MqttState.status.value.status == MqttState.Status.CONNECTED
        when {
            // 同一张网刚连上（服务启动时注册回调会立刻收到一次）：不用打断
            connected && !switched -> Unit
            // 换网后旧 socket 基本已失效，立即重连，不等 KeepAlive 超时
            connected -> forceReconnect("网络已切换")
            // 正在退避重试：别等满 60s，立刻再试一次
            else -> retrySignal.trySend(Unit)
        }
    }

    /**
     * 用户解锁 / 亮屏时立刻补一次：休眠期间错过的消息在重连后马上弹出，
     * 不用等下一次 15 分钟看门狗。
     */
    private fun registerWakeupReceiver() {
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                when (intent.action) {
                    Intent.ACTION_USER_PRESENT,
                    Intent.ACTION_SCREEN_ON,
                    -> {
                        val status = MqttState.status.value.status
                        if (status == MqttState.Status.CONNECTED) {
                            if (!MqttState.isConnectionAlive()) {
                                forceReconnect("亮屏/解锁：连接假死")
                            } else {
                                publishPresenceNow("亮屏/解锁")
                            }
                        } else {
                            retrySignal.trySend(Unit)
                        }
                    }
                }
            }
        }

        val filter = IntentFilter().apply {
            addAction(Intent.ACTION_USER_PRESENT)
            addAction(Intent.ACTION_SCREEN_ON)
        }
        runCatching {
            ContextCompat.registerReceiver(
                this,
                receiver,
                filter,
                ContextCompat.RECEIVER_NOT_EXPORTED,
            )
        }.onSuccess { wakeupReceiver = receiver }
            .onFailure { Log.w(TAG, "注册亮屏/解锁监听失败", it) }
    }

    private fun unregisterWakeupReceiver() {
        val receiver = wakeupReceiver ?: return
        wakeupReceiver = null
        runCatching { unregisterReceiver(receiver) }
            .onFailure { Log.w(TAG, "注销亮屏/解锁监听失败", it) }
    }

    /**
     * 链路假死兜底：心跳协程被系统冻结、或 socket 卡死但没触发断线回调时，
     * 连续 3 分钟没有成功收发就主动重连。
     */
    private fun startLivenessWatchdog() {
        scope.launch {
            while (isActive) {
                delay(LIVENESS_CHECK_MS)
                val snapshot = MqttState.status.value
                if (snapshot.status == MqttState.Status.CONNECTED &&
                    !MqttState.isConnectionAlive()
                ) {
                    Log.w(TAG, "连接超过 ${MqttState.CONNECT_STALE_MS}ms 无成功收发，主动重连")
                    forceReconnect("连接假死")
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    // 前台通知
    // ---------------------------------------------------------------------

    private fun observeStatus() {
        scope.launch {
            MqttState.status.collect { snapshot ->
                startForegroundCompat(serviceNotification(snapshot.status, snapshot.detail))
            }
        }
    }

    private fun serviceNotification(status: MqttState.Status, detail: String): Notification =
        NotificationHelper.serviceNotification(this, statusLabel(status), detail)

    private fun statusLabel(status: MqttState.Status): String = getString(
        when (status) {
            MqttState.Status.STOPPED -> R.string.status_stopped
            MqttState.Status.CONNECTING -> R.string.status_connecting
            MqttState.Status.CONNECTED -> R.string.status_connected
            MqttState.Status.RECONNECTING -> R.string.status_reconnecting
            MqttState.Status.FAILED -> R.string.status_failed
        }
    )

    private fun startForegroundCompat(notification: Notification) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            ServiceCompat.startForeground(
                this,
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE,
            )
        } else {
            // API 26–33：类型取自 manifest 声明，避免传 0 触发框架校验歧义
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    companion object {
        private const val TAG = "MqttService"
        private const val NOTIFICATION_ID = 1001
        private const val KEEP_ALIVE_SECONDS = 60
        private const val MAX_BACKOFF_MS = 60_000L
        private const val PRESENCE_HEARTBEAT_MS = 60_000L
        private const val PRESENCE_MIN_INTERVAL_MS = 20_000L
        private const val PRESENCE_PUBLISH_TIMEOUT_MS = 10_000L
        private const val LIVENESS_CHECK_MS = 30_000L
        private const val NETWORK_DEBOUNCE_MS = 3_000L
        private const val CONNECT_WAKE_LOCK_TIMEOUT_MS = 3 * 60_000L
        private const val PERSIST_DEBOUNCE_MS = 200L

        const val ACTION_START = "com.xct258.mqttnotify.action.START"
        const val ACTION_STOP = "com.xct258.mqttnotify.action.STOP"
        const val ACTION_RECONNECT = "com.xct258.mqttnotify.action.RECONNECT"

        /** 启动前台服务（Activity / BootReceiver 调用），同时打开看门狗。 */
        fun start(context: Context) {
            MqttWatchdog.setEnabled(context, true)
            val intent = Intent(context, MqttForegroundService::class.java).setAction(ACTION_START)
            runCatching { ContextCompat.startForegroundService(context, intent) }
                .onFailure { Log.w(TAG, "启动前台服务失败", it) }
        }

        /** 看门狗闹钟到点后拉起服务；ACTION_WATCHDOG 会再次校验用户是否仍想接收。 */
        fun startFromWatchdog(context: Context) {
            val intent = Intent(context, MqttForegroundService::class.java)
                .setAction(MqttWatchdog.ACTION_WATCHDOG)
            runCatching { ContextCompat.startForegroundService(context, intent) }
                .onFailure { Log.w(TAG, "看门狗拉起前台服务失败", it) }
        }

        /** 停止服务并断开连接，同时关闭看门狗（用户明确不想再收）。 */
        fun stop(context: Context) {
            MqttWatchdog.setEnabled(context, false)
            context.stopService(Intent(context, MqttForegroundService::class.java))
        }
    }
}

/** 把 HiveMQ 的 [CompletableFuture] 桥接进协程，等待时切到 IO 线程避免阻塞调度器。 */
private suspend fun <T> CompletableFuture<T>.await(): T = withContext(Dispatchers.IO) { get() }

/** 带超时的版本，用于清理阶段等不能无限等待的场景。 */
private suspend fun <T> CompletableFuture<T>.await(timeoutMs: Long): T =
    withContext(Dispatchers.IO) { get(timeoutMs, TimeUnit.MILLISECONDS) }
