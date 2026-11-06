package com.xct258.mqttnotify

import android.app.Application
import com.xct258.mqttnotify.mqtt.AlertStore
import com.xct258.mqttnotify.mqtt.MqttState
import com.xct258.mqttnotify.notify.NotificationHelper
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking

class MqttNotifyApp : Application() {
    override fun onCreate() {
        super.onCreate()
        // 提前建好通知渠道，服务起来即有渠道可用
        NotificationHelper.createChannels(this)

        // 历史消息量很小，启动时同步恢复，避免 UI/服务先跑起来后再覆盖新消息。
        runBlocking(Dispatchers.IO) {
            MqttState.restoreAlerts(AlertStore.load(this@MqttNotifyApp))
        }
    }
}
