package com.xct258.mqttnotify.mqtt

import android.content.Context
import android.os.Build
import org.json.JSONObject
import java.time.OffsetDateTime

/**
 * 客户端在线状态上报。
 *
 * 连接成功后向 `$presence/clients/{client_id}` 发布 retained JSON，断开时由
 * MQTT Will（LWT）把 `online` 改成 false；连接期间每 60 秒刷新一次。
 * 主题以 `$` 开头，不会被业务订阅的 `#` 通配符匹配到。
 */
object MqttPresence {
    const val TOPIC_PREFIX = "\$presence/clients"

    fun topicFor(clientId: String): String =
        "$TOPIC_PREFIX/${clientId.trim().replace('/', '_')}"

    fun suffixFor(clientId: String): String {
        val id = clientId.trim()
        // 新格式用 _ 分隔；兼容旧格式的 -，确保升级后仍能识别同一台设备。
        val separator = id.lastIndexOf('_').takeIf { it >= 0 } ?: id.lastIndexOf('-')
        return if (separator >= 0 && separator < id.length - 1) id.substring(separator + 1) else id
    }

    fun payload(context: Context, clientId: String, online: Boolean): ByteArray {
        val version = runCatching {
            context.packageManager.getPackageInfo(context.packageName, 0).versionName
        }.getOrNull().orEmpty().ifBlank { "unknown" }

        val name = listOf(Build.MANUFACTURER, Build.MODEL)
            .filter { !it.isNullOrBlank() }
            .joinToString(" ")
            .ifBlank { "Android" }

        return JSONObject()
            .put("client_id", clientId)
            .put("suffix", suffixFor(clientId))
            .put("name", name)
            .put("platform", "android")
            .put("version", version)
            .put("online", online)
            .put("ts", OffsetDateTime.now().toString())
            .toString()
            .toByteArray(Charsets.UTF_8)
    }
}
