package com.xct258.mqttnotify.mqtt

import android.content.Context
import android.util.Log
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/**
 * 提醒历史持久化：保留最近 30 天，最多 2000 条。
 * 文件位于应用私有目录 files/alerts.json，卸载或清除数据后清空。
 */
object AlertStore {
    private const val TAG = "AlertStore"
    private const val FILE_NAME = "alerts.json"

    const val RETENTION_MS = 30L * 24 * 60 * 60 * 1000
    const val MAX_RECORDS = 2000

    private val lock = Any()

    fun load(context: Context): List<MqttState.ReceivedAlert> {
        synchronized(lock) {
            val file = File(context.filesDir, FILE_NAME)
            if (!file.exists()) return emptyList()

            return runCatching {
                val cutoff = System.currentTimeMillis() - RETENTION_MS
                val array = JSONArray(file.readText())
                buildList {
                    for (i in 0 until array.length()) {
                        val item = array.optJSONObject(i) ?: continue
                        val receivedAt = item.optLong("receivedAt", 0L)
                        if (receivedAt < cutoff) continue
                        add(
                            MqttState.ReceivedAlert(
                                id = item.optLong("id", 0L),
                                kind = runCatching {
                                    MqttMessageParser.Kind.valueOf(
                                        item.optString("kind", MqttMessageParser.Kind.UNKNOWN.name)
                                    )
                                }.getOrDefault(MqttMessageParser.Kind.UNKNOWN),
                                taskId = if (item.isNull("taskId")) null else item.optInt("taskId"),
                                serverName = if (item.isNull("serverName")) {
                                    null
                                } else {
                                    item.optString("serverName").takeIf { it.isNotBlank() }
                                },
                                serviceName = if (item.isNull("serviceName")) {
                                    null
                                } else {
                                    item.optString("serviceName").takeIf { it.isNotBlank() }
                                },
                                status = if (item.isNull("status")) {
                                    null
                                } else {
                                    item.optString("status").takeIf { it.isNotBlank() }
                                },
                                topic = item.optString("topic"),
                                title = item.optString("title"),
                                body = item.optString("body"),
                                receivedAt = receivedAt,
                            )
                        )
                    }
                }.sortedByDescending { it.receivedAt }.take(MAX_RECORDS)
            }.onFailure {
                Log.w(TAG, "读取提醒历史失败", it)
            }.getOrDefault(emptyList())
        }
    }

    fun save(context: Context, alerts: List<MqttState.ReceivedAlert>) {
        synchronized(lock) {
            runCatching {
                val cutoff = System.currentTimeMillis() - RETENTION_MS
                val array = JSONArray()
                alerts.asSequence()
                    .filter { it.receivedAt >= cutoff }
                    .sortedByDescending { it.receivedAt }
                    .take(MAX_RECORDS)
                    .forEach { alert ->
                        array.put(
                            JSONObject()
                                .put("id", alert.id)
                                .put("kind", alert.kind.name)
                                .put("taskId", alert.taskId ?: JSONObject.NULL)
                                .put("serverName", alert.serverName ?: JSONObject.NULL)
                                .put("serviceName", alert.serviceName ?: JSONObject.NULL)
                                .put("status", alert.status ?: JSONObject.NULL)
                                .put("topic", alert.topic)
                                .put("title", alert.title)
                                .put("body", alert.body)
                                .put("receivedAt", alert.receivedAt)
                        )
                    }

                val target = File(context.filesDir, FILE_NAME)
                val temp = File(context.filesDir, "$FILE_NAME.tmp")
                temp.writeText(array.toString())
                if (!temp.renameTo(target)) {
                    temp.copyTo(target, overwrite = true)
                    temp.delete()
                }
            }.onFailure {
                Log.w(TAG, "保存提醒历史失败", it)
            }
        }
    }
}
