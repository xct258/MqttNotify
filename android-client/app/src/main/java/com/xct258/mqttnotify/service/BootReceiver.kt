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
 * 开机 / 应用更新后恢复接收。
 *
 * - [Intent.ACTION_BOOT_COMPLETED]：只有用户开启了「开机自动启动」才拉起；
 * - [Intent.ACTION_MY_PACKAGE_REPLACED]：升级前如果正在接收，升级后继续接收，
 *   不能因为一次覆盖安装就静默断掉；
 * - 其他情况继续由 [MqttWatchdog] 每 15 分钟兜底。
 *
 * BOOT_COMPLETED 是后台启动前台服务的允许豁免之一，无需用户交互。
 */
class BootReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action
        if (action != Intent.ACTION_BOOT_COMPLETED &&
            action != QUICKBOOT_POWERON &&
            action != Intent.ACTION_MY_PACKAGE_REPLACED
        ) {
            return
        }

        val pending = goAsync()
        val appContext = context.applicationContext
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val settings = SettingsRepository.get(appContext).settings.first()

                val shouldStart = when (action) {
                    // 覆盖安装：只要之前用户点过「启动接收」，升级后继续接收
                    Intent.ACTION_MY_PACKAGE_REPLACED ->
                        settings.autoStartOnBoot || MqttWatchdog.isEnabled(appContext)

                    // 重启：严格遵循「开机自动启动」开关
                    else -> settings.autoStartOnBoot
                }

                if (shouldStart && settings.isValid) {
                    MqttForegroundService.start(appContext)
                    Log.i(TAG, "$action：已拉起 MQTT 前台服务")
                } else if (!settings.isValid || !settings.autoStartOnBoot) {
                    // 配置不完整或用户关掉了开机自启：别让看门狗反复做无用唤醒
                    MqttWatchdog.setEnabled(appContext, false)
                    Log.i(TAG, "$action：不满足自动启动条件，保持停止")
                }
            } catch (e: Exception) {
                Log.w(TAG, "恢复接收失败", e)
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
