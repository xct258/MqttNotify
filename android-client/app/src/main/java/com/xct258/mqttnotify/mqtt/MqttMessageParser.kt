package com.xct258.mqttnotify.mqtt

/**
 * 下行消息解析。
 *
 * push-server 约定 payload 为**拼好的纯文本**，格式 `【待办提醒】{title}\n{message}`：
 *  - 首行若以 `【待办提醒】` 开头，其后即标题，剩余行为正文；
 *  - 无该前缀时整段作为正文，标题使用 [fallbackTitle]（由调用方从资源传入，便于本地化）；
 *  - 主题最后一段即 task_id（取不到则为 null，仅用于展示/去重）。
 *
 * 解析失败不抛异常：宁可展示原文，也不丢提醒。
 */
object MqttMessageParser {

    const val MARKER = "【待办提醒】"

    data class ParsedAlert(
        val taskId: Int?,
        val title: String,
        val body: String,
    )

    fun parse(topic: String, payload: ByteArray, fallbackTitle: String): ParsedAlert {
        // 统一换行符：服务端发 \n，但兼容 \r\n 更稳
        val raw = payload.toString(Charsets.UTF_8).replace("\r\n", "\n").trim()
        val taskId = topic.substringAfterLast('/').toIntOrNull()

        if (raw.isEmpty()) {
            return ParsedAlert(taskId, fallbackTitle, "")
        }

        val lines = raw.split('\n')
        val firstLine = lines.first()
        return if (firstLine.startsWith(MARKER)) {
            val title = firstLine.removePrefix(MARKER).trim().ifBlank { fallbackTitle }
            val body = lines.drop(1).joinToString("\n").trim()
            ParsedAlert(taskId, title, body)
        } else {
            ParsedAlert(taskId, fallbackTitle, raw)
        }
    }
}
