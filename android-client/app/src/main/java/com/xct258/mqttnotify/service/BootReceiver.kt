package com.xct258.mqttnotify.service

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.xct258.mqttnotify.data.SettingsRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

/**
 * 开机自启：若用户开启了「开机自动启动」且配置完整，则拉起前台服务。
 *
 * BOOT_COMPLETED 是后台启动前台服务的允许豁免之一，无需用户交互。
 */
class BootReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action
        if (action != Intent.ACTION_BOOT_COMPLETED && action != QUICKBOOT_POWERON) return

        val pending = goAsync()
        val appContext = context.applicationContext
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val settings = SettingsRepository.get(appContext).settings.first()
                if (settings.autoStartOnBoot && settings.isValid) {
                    MqttForegroundService.start(appContext)
                    Log.i(TAG, "开机自启：已拉起 MQTT 前台服务")
                }
            } catch (e: Exception) {
                Log.w(TAG, "开机自启失败", e)
            } finally {
                pending.finish()
            }
        }
    }

    private companion object {
        const val TAG = "BootReceiver"
        const val QUICKBOOT_POWERON = "android.intent.action.QUICKBOOT_POWERON"
    }
}
