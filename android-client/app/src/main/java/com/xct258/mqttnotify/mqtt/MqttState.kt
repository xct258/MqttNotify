package com.xct258.mqttnotify.mqtt

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.concurrent.atomic.AtomicLong

/**
 * 进程内的连接状态与最近提醒总线：服务写、UI 读。
 *
 * 纯内存（不落盘）：进程被杀后重置。这也符合“离线补拉由 REST 负责”的设计，
 * 本地不承担持久化职责。
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
        val taskId: Int?,
        val topic: String,
        val title: String,
        val body: String,
        val receivedAt: Long,
    )

    private const val MAX_KEEP = 50

    private val seq = AtomicLong(0)

    private val _status = MutableStateFlow(Snapshot())
    val status: StateFlow<Snapshot> = _status.asStateFlow()

    private val _alerts = MutableStateFlow<List<ReceivedAlert>>(emptyList())
    val alerts: StateFlow<List<ReceivedAlert>> = _alerts.asStateFlow()

    fun setStatus(status: Status, detail: String = "") {
        _status.value = Snapshot(status, detail, System.currentTimeMillis())
    }

    /** 追加一条提醒并返回它，便于调用方直接拿去发通知。 */
    fun addAlert(taskId: Int?, topic: String, title: String, body: String): ReceivedAlert {
        val item = ReceivedAlert(
            id = seq.incrementAndGet(),
            taskId = taskId,
            topic = topic,
            title = title,
            body = body,
            receivedAt = System.currentTimeMillis(),
        )
        _alerts.value = (listOf(item) + _alerts.value).take(MAX_KEEP)
        return item
    }

    fun clearAlerts() {
        _alerts.value = emptyList()
    }
}
