package com.xct258.mqttnotify.mqtt

/**
 * 下行消息解析。
 *
 * 协议 v2 按 `category` 分成两种卡片：
 *
 *  - 待办提醒（`【待办提醒】{title}\n{message}`）：
 *  - 服务通知（`【服务通知】{server}\n【服务】{title}\n【状态】{status}\n\n{message}`）。
 *
 * push-server 只会在一种格式内下发，客户端按首行 marker 分派。
 * 无法识别的旧格式落到 [Kind.UNKNOWN]，宁可按原文展示也不丢消息。
 */
object MqttMessageParser {

    const val TODO_MARKER = "【待办提醒】"

    /** 兼容旧测试/调用方的名字。 */
    const val MARKER = TODO_MARKER

    const val SERVICE_MARKER = "【服务通知】"
    const val SERVICE_FIELD = "【服务】"
    const val STATUS_FIELD = "【状态】"

    enum class Kind { TODO, SERVICE, UNKNOWN }

    data class ParsedAlert(
        val kind: Kind,
        val taskId: Int?,
        val serverName: String?,
        val serviceName: String?,
        val status: String?,
        val title: String,
        val body: String,
    )

    fun parse(topic: String, payload: ByteArray, fallbackTitle: String): ParsedAlert {
        // 统一换行符：服务端发 \n，但兼容 \r\n 更稳
        val raw = payload.toString(Charsets.UTF_8).replace("\r\n", "\n").trim()
        val taskId = topic.substringAfterLast('/').toIntOrNull()

        if (raw.isEmpty()) {
            return ParsedAlert(
                kind = Kind.UNKNOWN,
                taskId = taskId,
                serverName = null,
                serviceName = null,
                status = null,
                title = fallbackTitle,
                body = "",
            )
        }

        val lines = raw.split('\n')
        val firstLine = lines.first()

        return when {
            firstLine.startsWith(TODO_MARKER) -> parseTodo(lines, taskId, fallbackTitle)
            firstLine.startsWith(SERVICE_MARKER) -> parseService(lines, taskId, fallbackTitle)
            else -> ParsedAlert(
                kind = Kind.UNKNOWN,
                taskId = taskId,
                serverName = null,
                serviceName = null,
                status = null,
                title = fallbackTitle,
                body = raw,
            )
        }
    }

    private fun parseTodo(
        lines: List<String>,
        taskId: Int?,
        fallbackTitle: String,
    ): ParsedAlert {
        val title = lines.first()
            .removePrefix(TODO_MARKER)
            .trim()
            .ifBlank { fallbackTitle }
        val body = lines.drop(1).joinToString("\n").trim()
        return ParsedAlert(
            kind = Kind.TODO,
            taskId = taskId,
            serverName = null,
            serviceName = null,
            status = null,
            title = title,
            body = body,
        )
    }

    private fun parseService(
        lines: List<String>,
        taskId: Int?,
        fallbackTitle: String,
    ): ParsedAlert {
        val serverName = lines.first()
            .removePrefix(SERVICE_MARKER)
            .trim()
            .takeIf { it.isNotBlank() }

        var serviceName: String? = null
        var status: String? = null
        var bodyStart = 1

        while (bodyStart < lines.size) {
            val line = lines[bodyStart]
            if (line.isBlank()) {
                bodyStart++
                break
            }
            when {
                line.startsWith(SERVICE_FIELD) ->
                    serviceName = line.removePrefix(SERVICE_FIELD).trim().takeIf { it.isNotBlank() }

                line.startsWith(STATUS_FIELD) ->
                    status = line.removePrefix(STATUS_FIELD).trim().takeIf { it.isNotBlank() }

                // 没写空行时，遇到正文第一行就停，保证内容不丢
                else -> break
            }
            bodyStart++
        }

        val body = lines.drop(bodyStart).joinToString("\n").trim()
        return ParsedAlert(
            kind = Kind.SERVICE,
            taskId = taskId,
            serverName = serverName,
            serviceName = serviceName,
            status = status,
            title = serverName ?: fallbackTitle,
            body = body,
        )
    }
}
