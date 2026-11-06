package com.xct258.mqttnotify.mqtt

import android.os.SystemClock
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.concurrent.atomic.AtomicLong

/**
 * 进程内的连接状态与提醒总线：服务写、UI 读。
 *
 * 提醒历史由 AlertStore 持久化，保留最近 30 天。
 */
object MqttState {

    enum class Status { STOPPED, CONNECTING, CONNECTED, RECONNECTING, FAILED }

    data class Snapshot(
        val status: Status = Status.STOPPED,
        val detail: String = "",
        val changedAt: Long = 0L,
    )

    data class ReceivedAlert(
        val id: Long,
        val kind: MqttMessageParser.Kind,
        val taskId: Int?,
        val serverName: String?,
        val serviceName: String?,
        val status: String?,
        val topic: String,
        val title: String,
        val body: String,
        val receivedAt: Long,
    )

    private val seq = AtomicLong(0)

    private val _status = MutableStateFlow(Snapshot())
    val status: StateFlow<Snapshot> = _status.asStateFlow()

    private val _alerts = MutableStateFlow<List<ReceivedAlert>>(emptyList())
    val alerts: StateFlow<List<ReceivedAlert>> = _alerts.asStateFlow()

    /**
     * 最近一次成功收发 MQTT 报文的时间（[SystemClock.elapsedRealtime]，含休眠）。
     *
     * 看门狗用它判断「状态还写着已连接，但连接其实已经假死」的情况。
     * 进程被杀后该值归零，下次唤醒必然触发重连。
     */
    @Volatile
    var lastActivityAt: Long = 0L
        private set

    fun setStatus(status: Status, detail: String = "") {
        _status.value = Snapshot(status, detail, System.currentTimeMillis())
    }

    /** 记录一次可证明链路仍然可用的收发（连接成功、收到消息、心跳成功）。 */
    fun markActivity() {
        lastActivityAt = SystemClock.elapsedRealtime()
    }

    /** 已连接且最近 [CONNECT_STALE_MS] 内有过成功收发，视为链路健康。 */
    fun isConnectionAlive(now: Long = SystemClock.elapsedRealtime()): Boolean =
        lastActivityAt > 0L && now - lastActivityAt <= CONNECT_STALE_MS

    /** 追加一条提醒并返回它，便于调用方直接拿去发通知。 */
    fun addAlert(parsed: MqttMessageParser.ParsedAlert, topic: String): ReceivedAlert {
        val item = ReceivedAlert(
            id = seq.incrementAndGet(),
            kind = parsed.kind,
            taskId = parsed.taskId,
            serverName = parsed.serverName,
            serviceName = parsed.serviceName,
            status = parsed.status,
            topic = topic,
            title = parsed.title,
            body = parsed.body,
            receivedAt = System.currentTimeMillis(),
        )
        val cutoff = System.currentTimeMillis() - AlertStore.RETENTION_MS
        _alerts.value = (listOf(item) + _alerts.value)
            .filter { it.receivedAt >= cutoff }
            .take(AlertStore.MAX_RECORDS)
        return item
    }

    /** 启动时恢复历史，并避免新消息 id 和旧记录冲突。 */
    fun restoreAlerts(alerts: List<ReceivedAlert>) {
        val cutoff = System.currentTimeMillis() - AlertStore.RETENTION_MS
        val restored = alerts
            .filter { it.receivedAt >= cutoff }
            .sortedByDescending { it.receivedAt }
            .take(AlertStore.MAX_RECORDS)
        _alerts.value = restored
        seq.set(restored.maxOfOrNull { it.id } ?: 0L)
    }

    fun clearAlerts() {
        _alerts.value = emptyList()
    }

    /** 心跳 60s 一次；连续 3 个周期没有成功收发就认为链路假死。 */
    const val CONNECT_STALE_MS: Long = 180_000L
}
