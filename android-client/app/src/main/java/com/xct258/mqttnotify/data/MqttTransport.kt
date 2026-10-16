package com.xct258.mqttnotify.data

/**
 * 下行传输方式。
 *
 * 对应 push-server 里 Mosquitto 的监听（见其 `容器构建脚本/start.sh`）：
 *
 * ```
 * listener 1883 127.0.0.1
 *    allow_anonymous true
 * listener 9001 0.0.0.0
 *    protocol websockets
 *    allow_anonymous true
 * ```
 *
 * - [TCP] `mqtt://host:1883` —— 注意 1883 **只绑在 127.0.0.1**，
 *   手机在局域网里连不上，除非自己改配置；
 * - [WS]  `ws://host:9001`  —— push-server 默认对外开放的 websockets 监听；
 * - [WSS] `wss://host/mqtt` —— WS 之上叠加 TLS，通常由 nginx/caddy 反代 9001 终止 TLS。
 */
enum class MqttTransport(
    /** URL scheme，也用于界面展示。 */
    val scheme: String,
    /** 推荐端口；用户切换传输方式且端口仍是旧默认值时会自动跟随。 */
    val defaultPort: Int,
    val isWebSocket: Boolean,
    val isTls: Boolean,
) {
    TCP("mqtt", 1883, isWebSocket = false, isTls = false),
    WS("ws", 9001, isWebSocket = true, isTls = false),
    WSS("wss", 443, isWebSocket = true, isTls = true),
    ;

    /** 展示用连接地址，如 `wss://example.com:443/mqtt`、`mqtt://10.0.0.2:1883`。 */
    fun displayUrl(host: String, port: Int, wsPath: String): String {
        val displayHost = host.ifBlank { "<broker-host>" }
        val path = if (!isWebSocket) {
            ""
        } else {
            wsPath.trim().let { if (it.startsWith("/")) it else "/$it" }
        }
        return "$scheme://$displayHost:$port$path"
    }
}
