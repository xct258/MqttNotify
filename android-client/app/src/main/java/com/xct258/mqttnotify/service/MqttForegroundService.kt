package com.xct258.mqttnotify.service

import android.app.Notification
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
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
import com.xct258.mqttnotify.mqtt.InsecureSsl
import com.xct258.mqttnotify.mqtt.MqttMessageParser
import com.xct258.mqttnotify.mqtt.MqttState
import com.xct258.mqttnotify.notify.NotificationHelper
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.util.UUID
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit
import javax.net.ssl.HostnameVerifier

/**
 * 常驻 MQTT 前台服务。
 *
 * 职责：
 *  1. 以前台服务形式保活，读取 [SettingsRepository] 中的配置；
 *  2. 建立 MQTT 3.1.1 连接并订阅 `{base}/#`（QoS 1、clean session、KeepAlive 60s）；
 *  3. 收到消息即解析、记录到 [MqttState] 并弹出系统通知；
 *  4. 断线由 HiveMQ 自动重连（指数退避），首次连接失败则本地退避重试。
 *
 * 配置变更后无需外部通知：服务持续 collect 设置流，变化即重连。
 */
class MqttForegroundService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    @Volatile
    private var client: Mqtt3AsyncClient? = null

    private var settingsJob: Job? = null

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
        observeStatus()
        observeSettings()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }
        // 重复 start 时刷新前台通知（满足 startForeground 时限）并确保监听在跑
        val snapshot = MqttState.status.value
        startForegroundCompat(serviceNotification(snapshot.status, snapshot.detail))
        observeSettings()
        return START_STICKY
    }

    override fun onDestroy() {
        scope.cancel()
        runBlocking { withTimeoutOrNull(1_000L) { teardownClient() } }
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
            SettingsRepository.get(this@MqttForegroundService).settings
                .map { it.normalized() }
                .distinctUntilChanged()
                .collectLatest { settings -> connectWithRetry(settings) }
        }
    }

    /**
     * 惰性刷新设置再连接：
     * 先拆掉旧连接，未配置则提示后退出；已配置则循环重试直到连上为止
     * （连上后交给 HiveMQ 的自动重连接管）。
     */
    private suspend fun connectWithRetry(settings: AppSettings) {
        teardownClient()
        if (!settings.isValid) {
            MqttState.setStatus(MqttState.Status.FAILED, "尚未配置 Broker 地址")
            return
        }

        var backoffMs = 2_000L
        while (currentCoroutineContext().isActive) {
            try {
                establish(settings)
                return
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "连接失败，${backoffMs}ms 后重试", e)
                teardownClient()
                MqttState.setStatus(MqttState.Status.FAILED, e.message ?: e.javaClass.simpleName)
                delay(backoffMs)
                backoffMs = (backoffMs * 2).coerceAtMost(MAX_BACKOFF_MS)
            }
        }
    }

    private suspend fun establish(settings: AppSettings) {
        val gen = generation
        val clientId = settings.clientId.ifBlank { "android-" + UUID.randomUUID().toString().take(8) }

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
            .automaticReconnectWithDefaultConfig()
            .addConnectedListener {
                if (gen == generation) {
                    MqttState.setStatus(MqttState.Status.CONNECTED, endpoint)
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
            .cleanSession(true)
            .keepAlive(KEEP_ALIVE_SECONDS)
            .send()
            .await()

        newClient.subscribeWith()
            .topicFilter(settings.subscribeTopic)
            .qos(MqttQos.AT_LEAST_ONCE)
            .callback { publish -> handlePublish(publish) }
            .send()
            .await()

        MqttState.setStatus(
            MqttState.Status.CONNECTED,
            "$endpoint · ${settings.subscribeTopic}",
        )
        Log.i(TAG, "已连接 $endpoint，订阅 ${settings.subscribeTopic}")
    }

    private suspend fun teardownClient() {
        generation++
        val old = client ?: return
        client = null
        withContext(Dispatchers.IO) {
            runCatching { old.disconnect().get(1, TimeUnit.SECONDS) }
        }
    }

    private fun handlePublish(publish: Mqtt3Publish) {
        val topic = publish.topic.toString()
        val parsed = MqttMessageParser.parse(
            topic,
            publish.payloadAsBytes,
            getString(R.string.default_alert_title),
        )
        val alert = MqttState.addAlert(parsed.taskId, topic, parsed.title, parsed.body)
        Log.i(TAG, "收到提醒 [$topic] ${parsed.title}")
        // 通知涉及 binder 调用，挪出 Netty 线程
        scope.launch { NotificationHelper.notifyAlert(this@MqttForegroundService, alert) }
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

        const val ACTION_START = "com.xct258.mqttnotify.action.START"
        const val ACTION_STOP = "com.xct258.mqttnotify.action.STOP"

        /** 启动前台服务（Activity / BootReceiver 调用）。 */
        fun start(context: Context) {
            val intent = Intent(context, MqttForegroundService::class.java).setAction(ACTION_START)
            ContextCompat.startForegroundService(context, intent)
        }

        /** 停止服务并断开连接。 */
        fun stop(context: Context) {
            context.stopService(Intent(context, MqttForegroundService::class.java))
        }
    }
}

/** 把 HiveMQ 的 [CompletableFuture] 桥接进协程，等待时切到 IO 线程避免阻塞调度器。 */
private suspend fun <T> CompletableFuture<T>.await(): T = withContext(Dispatchers.IO) { get() }
