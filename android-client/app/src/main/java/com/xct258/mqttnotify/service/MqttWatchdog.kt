package com.xct258.mqttnotify.service

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.SystemClock
import android.util.Log
import com.xct258.mqttnotify.data.SettingsRepository
import com.xct258.mqttnotify.mqtt.MqttState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

/**
 * 后台看门狗。
 *
 * 前台服务已经能挡住大部分清理，但国产 ROM / 极端内存压力仍可能把进程杀掉。
 * 这里用 [AlarmManager] 每 15 分钟做一次低成本自检：
 *
 *  - 服务进程还活着且链路健康：什么都不做，只排下一次闹钟；
 *  - 进程已被杀（[MqttState] 回到 STOPPED）：重新拉起前台服务；
 *  - 进程活着但状态是「已连接」且超过 3 分钟没有任何成功收发：主动重连。
 *
 * 闹钟用 `setExactAndAllowWhileIdle`（有精确闹钟权限时）或
 * `setAndAllowWhileIdle`（没有权限时）投递，因此在 Doze 深度休眠中也能唤醒。
 * 精确闹钟广播是 Android 12+ 允许后台启动前台服务的豁免场景之一。
 */
object MqttWatchdog {

    private const val TAG = "MqttWatchdog"
    private const val PREFS = "mqtt_watchdog"
    private const val KEY_ENABLED = "enabled"
    private const val REQUEST_CODE = 0x10A1

    /** 自检间隔。Doze 下 allow-while-idle 闹钟最快约 9 分钟一次，15 分钟留足余量。 */
    const val INTERVAL_MS = 15L * 60 * 1000

    const val ACTION_WATCHDOG = "com.xct258.mqttnotify.action.WATCHDOG"

    /** 用户是否希望持续接收（点「启动接收」为 true，点「停止接收」为 false）。 */
    fun isEnabled(context: Context): Boolean =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getBoolean(KEY_ENABLED, false)

    fun setEnabled(context: Context, enabled: Boolean) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putBoolean(KEY_ENABLED, enabled)
            .apply()

        if (enabled) {
            schedule(context)
        } else {
            cancel(context)
        }
    }

    /** 排下一次自检（覆盖旧闹钟，保证同一时刻只有一个）。 */
    fun schedule(context: Context) {
        // 用 ELAPSED_REALTIME：用户改系统时间不会让闹钟漂移；重启后由 BootReceiver 重排。
        val triggerAt = SystemClock.elapsedRealtime() + INTERVAL_MS
        val alarmManager = context.getSystemService(AlarmManager::class.java) ?: return
        val pendingIntent = pendingIntent(context)

        runCatching {
            when {
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
                    alarmManager.canScheduleExactAlarms() ->
                    alarmManager.setExactAndAllowWhileIdle(
                        AlarmManager.ELAPSED_REALTIME_WAKEUP,
                        triggerAt,
                        pendingIntent,
                    )

                Build.VERSION.SDK_INT >= Build.VERSION_CODES.M ->
                    alarmManager.setAndAllowWhileIdle(
                        AlarmManager.ELAPSED_REALTIME_WAKEUP,
                        triggerAt,
                        pendingIntent,
                    )

                else ->
                    alarmManager.set(
                        AlarmManager.ELAPSED_REALTIME_WAKEUP,
                        triggerAt,
                        pendingIntent,
                    )
            }
        }.onFailure { error ->
            Log.w(TAG, "排精确看门狗闹钟失败，降级为非精确闹钟", error)
            runCatching {
                alarmManager.setAndAllowWhileIdle(
                    AlarmManager.ELAPSED_REALTIME_WAKEUP,
                    triggerAt,
                    pendingIntent,
                )
            }.onFailure { Log.w(TAG, "排看门狗闹钟失败", it) }
        }
    }

    fun cancel(context: Context) {
        val alarmManager = context.getSystemService(AlarmManager::class.java) ?: return
        runCatching { alarmManager.cancel(pendingIntent(context)) }
            .onFailure { Log.w(TAG, "取消看门狗闹钟失败", it) }
    }

    /**
     * 闹钟到达后的自检：仅当「用户仍希望接收 + 配置有效」时才拉服务。
     *
     * 运行在 IO 线程；Receiver 已用 goAsync() 保活。
     */
    suspend fun ensureService(context: Context) {
        if (!isEnabled(context)) {
            cancel(context)
            return
        }

        val settings = runCatching { SettingsRepository.get(context).settings.first() }
            .getOrElse {
                Log.w(TAG, "读取配置失败，跳过本次自检", it)
                return
            }
        if (!settings.isValid) {
            Log.w(TAG, "配置不完整，跳过本次自检")
            return
        }

        val snapshot = MqttState.status.value
        if (snapshot.status == MqttState.Status.CONNECTED && MqttState.isConnectionAlive()) {
            Log.d(TAG, "链路健康，无需干预")
            return
        }
        if (snapshot.status == MqttState.Status.CONNECTING ||
            snapshot.status == MqttState.Status.RECONNECTING
        ) {
            Log.d(TAG, "服务正在连接/重连，无需干预")
            return
        }

        Log.i(TAG, "链路不健康（${snapshot.status}），拉起前台服务自检")
        MqttForegroundService.startFromWatchdog(context)
    }

    private fun pendingIntent(context: Context): PendingIntent {
        val intent = Intent(context, MqttWatchdogReceiver::class.java)
            .setAction(ACTION_WATCHDOG)
        return PendingIntent.getBroadcast(
            context,
            REQUEST_CODE,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }
}

/**
 * 看门狗闹钟接收器。自己先排下一次，避免单次异常导致看门狗彻底停摆。
 */
class MqttWatchdogReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != MqttWatchdog.ACTION_WATCHDOG) return

        if (!MqttWatchdog.isEnabled(context)) {
            MqttWatchdog.cancel(context)
            return
        }

        MqttWatchdog.schedule(context)

        val pending = goAsync()
        val appContext = context.applicationContext
        CoroutineScope(Dispatchers.IO).launch {
            try {
                MqttWatchdog.ensureService(appContext)
            } catch (e: Exception) {
                Log.w(TAG, "看门狗自检失败", e)
            } finally {
                pending.finish()
            }
        }
    }

    private companion object {
        const val TAG = "MqttWatchdog"
    }
}
