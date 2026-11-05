package com.xct258.mqttnotify.data

/**
 * 客户端连接配置。
 *
 * 字段含义与 push-server 的 MQTT 接入文档对应（见仓库 MQTT 接口调用文档.md）：
 *  - [host] / [port] / [transport]：broker 地址与传输方式；
 *  - [topicBase]：下行主题的 base，默认 taskops/todo；实际订阅 [subscribeTopic]；
 *  - [serverName]：来源服务名（如 taskops），用于后续 REST 补拉，不影响订阅；
 *  - [clientId]：自定义 Client ID 前缀；最终 ID 为 `{前缀}_{固定随机后缀}`，留空则为 `android_xxxxxxxx`；
 *  - [wsPath]：WS/WSS 的 serverPath（默认 /mqtt）；
 *  - [allowInsecureTls]：WSS 下跳过证书链校验（仅内网自签证书用）。
 */
data class AppSettings(
    val host: String = DEFAULT_HOST,
    val port: Int = DEFAULT_PORT,
    val topicBase: String = DEFAULT_TOPIC,
    val serverName: String = DEFAULT_SERVER,
    /** 用户自定义的 Client ID 前缀；留空时使用 `android`。 */
    val clientId: String = "",
    /** 安装时生成并持久化的随机后缀，清空数据/重装后才会变化。 */
    val clientIdSuffix: String = "",
    val autoStartOnBoot: Boolean = true,
    /**
     * 默认走公网 WSS：push-server 的 Mosquitto 只把 TCP 1883 绑在 127.0.0.1，
     * 手机跨机连不上；公网入口是 `wss://push-server.o1-1.xct258.top/mqtt`。
     */
    val transport: MqttTransport = DEFAULT_TRANSPORT,
    val wsPath: String = DEFAULT_WS_PATH,
    val allowInsecureTls: Boolean = false,
) {
    /**
     * WebSocket 路径规范化：空则回落到默认 `/mqtt`，缺前导斜杠则补上。
     * HiveMQ 的 serverPath 必须以 `/` 开头，否则会在建连时抛异常。
     */
    val effectiveWsPath: String
        get() = wsPath.trim().let {
            when {
                it.isEmpty() -> DEFAULT_WS_PATH
                it.startsWith("/") -> it
                else -> "/$it"
            }
        }

    /** 最终 MQTT Client ID：`{自定义或 android}_{固定随机后缀}`。 */
    val effectiveClientId: String
        get() {
            val prefix = clientId.trim().ifBlank { "android" }
            return if (clientIdSuffix.isBlank()) prefix else "${prefix}_${clientIdSuffix.trim()}"
        }

    /** 是否已配置到可连接的程度。 */
    val isValid: Boolean
        get() = host.isNotBlank() && port in 1..65535 && topicBase.isNotBlank()

    /** 通配订阅主题，例如 `taskops/todo/#`；base 填 `#` 时为 `#`。 */
    val subscribeTopic: String
        get() = buildSubscribeTopic(topicBase)

    /** 规范化：去空白、补 wsPath、清掉与传输方式无关的开关。 */
    fun normalized(): AppSettings = copy(
        host = host.trim(),
        topicBase = normalizeTopicBase(topicBase),
        serverName = serverName.trim(),
        clientId = clientId.trim(),
        wsPath = effectiveWsPath,
        allowInsecureTls = allowInsecureTls && transport.isTls,
    )

    companion object {
        const val CONFIG_VERSION = 2
        const val DEFAULT_HOST = "push-server.o1-1.xct258.top"
        const val DEFAULT_PORT = 443
        const val DEFAULT_TOPIC = "taskops/todo"
        const val DEFAULT_SERVER = "taskops"
        const val DEFAULT_WS_PATH = "/mqtt"
        val DEFAULT_TRANSPORT = MqttTransport.WSS

        /**
         * 规范化订阅主题的 base：既接受 `taskops/todo`，也接受直接粘贴
         * `taskops/todo/#`，避免再拼一次变成非法的 `taskops/todo/#/#`。
         * 单独的 `#` 表示订阅所有主题，原样保留。
         */
        fun normalizeTopicBase(topicBase: String): String {
            val topic = topicBase.trim().trimEnd('/')
            if (topic == "#") return "#"
            return if (topic.endsWith("/#")) topic.dropLast(2).trimEnd('/') else topic
        }

        /** 根据 base 生成实际订阅的通配主题。 */
        fun buildSubscribeTopic(topicBase: String): String {
            val topic = normalizeTopicBase(topicBase)
            return if (topic.isEmpty() || topic == "#") "#" else "$topic/#"
        }
    }
}
