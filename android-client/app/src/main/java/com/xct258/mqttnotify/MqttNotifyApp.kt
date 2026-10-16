package com.xct258.mqttnotify

import android.app.Application
import com.xct258.mqttnotify.notify.NotificationHelper

class MqttNotifyApp : Application() {
    override fun onCreate() {
        super.onCreate()
        // 提前建好通知渠道，服务起来即有渠道可用
        NotificationHelper.createChannels(this)
    }
}
