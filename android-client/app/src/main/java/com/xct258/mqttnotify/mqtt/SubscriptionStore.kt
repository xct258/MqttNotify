package com.xct258.mqttnotify.mqtt

import android.content.Context

/**
 * 记录上一次成功订阅的主题。
 *
 * Android 端使用 MQTT 持久会话（cleanSession=false）让 broker 在离线期间
 * 排队 QoS1 消息。持久会话会保留旧订阅，所以用户修改订阅主题后，必须显式
 * 取消旧主题，否则新旧主题会同时收信。
 */
internal object SubscriptionStore {
    private const val PREFS = "mqtt_subscription"
    private const val KEY_TOPIC = "last_topic"

    fun lastTopic(context: Context): String? =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getString(KEY_TOPIC, null)
            ?.takeIf { it.isNotBlank() }

    fun setLastTopic(context: Context, topic: String) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_TOPIC, topic)
            .apply()
    }
}
