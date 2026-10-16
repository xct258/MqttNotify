package com.xct258.mqttnotify.data

/**
 * 客户端连接配置。
 *
 * 字段含义与 push-server 的 MQTT 接入文档对应（见仓库 MQTT 接口调用文档.md）：
 *  - [host] / [port] / [transport]：broker 地址与传输方式；
 *  - [topicBase]：下行主题的 base，默认 taskops/todo；实际订阅 [subscribeTopic]；
 *  - [serverName]：来源服务名（如 taskops），用于后续 REST 补拉，不影响订阅；
 *  - [clientId]：MQTT Client ID，留空则启动时自动生成，同 ID 重复连接会顶掉旧连接；
 *  - [wsPath]：WS/WSS 的 serverPath（默认 /mqtt）；
 *  - [allowInsecureTls]：WSS 下跳过证书链校验（仅内网自签证书用）。
 */
data class AppSettings(
    val host: String = DEFAULT_HOST,
    val port: Int = DEFAULT_PORT,
    val topicBase: String = DEFAULT_TOPIC,
    val serverName: String = DEFAULT_SERVER,
    val clientId: String = "",
    val autoStartOnBoot: Boolean = true,
    val transport: MqttTransport = MqttTransport.TCP,
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

    /** 是否已配置到可连接的程度。 */
    val isValid: Boolean
        get() = host.isNotBlank() && port in 1..65535 && topicBase.isNotBlank()

    /** 通配订阅主题，例如 `taskops/todo/#`。 */
    val subscribeTopic: String
        get() = "${topicBase.trim().trimEnd('/')}/#"

    /** 规范化：去空白、补 wsPath、清掉与传输方式无关的开关。 */
    fun normalized(): AppSettings = copy(
        host = host.trim(),
        topicBase = topicBase.trim().trimEnd('/'),
        serverName = serverName.trim(),
        clientId = clientId.trim(),
        wsPath = effectiveWsPath,
        allowInsecureTls = allowInsecureTls && transport.isTls,
    )

    companion object {
        const val DEFAULT_HOST = ""
        const val DEFAULT_PORT = 1883
        const val DEFAULT_TOPIC = "taskops/todo"
        const val DEFAULT_SERVER = "taskops"
        const val DEFAULT_WS_PATH = "/mqtt"
    }
}
