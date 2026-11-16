package com.xct258.mqttnotify.service

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.SystemClock
import android.provider.Settings
import android.util.Log
import com.xct258.mqttnotify.BuildConfig
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
 * 这里用 [AlarmManager] 做低成本自检：
 *
 *  - 服务进程还活着且链路健康：什么都不做，只排下一次闹钟；
 *  - 进程已被杀（[MqttState] 回到 STOPPED）：重新拉起前台服务；
 *  - 进程活着但连接假死 / 卡在连接中：立即踢一脚重连；
 *  - 拉起前台服务被系统拦截（没有精确闹钟权限时可能发生）：发一条可点击的恢复通知。
 *
 * 闹钟用 `setExactAndAllowWhileIdle`（有精确闹钟权限时，约 9 分钟一次）或
 * `setAndAllowWhileIdle`（没有权限时，约 15 分钟一次）投递，因此在 Doze 深度休眠中
 * 也能唤醒。精确闹钟广播是 Android 12+ 允许后台启动前台服务的豁免场景之一。
 *
 * 注意：`ensureScheduled()` 只在“当前没有未来闹钟”时补排，避免收到消息 / 亮屏 / 重连
 * 时不断把闹钟往后推，导致看门狗永远不触发。
 */
object MqttWatchdog {

    private const val TAG = "MqttWatchdog"
    private const val PREFS = "mqtt_watchdog"
    private const val KEY_ENABLED = "enabled"
    private const val KEY_NEXT_AT = "next_at_elapsed"
    private const val KEY_INTERVAL_MS = "interval_ms"
    private const val KEY_SCHEDULED_EXACT = "scheduled_exact"
    private const val REQUEST_CODE = 0x10A1
    private const val DEBUG_INTERVAL_SETTING = "mqttnotify_watchdog_ms"

    /** 有精确闹钟权限时，Doze 下最快约 9 分钟一次，留一点余量。 */
    const val EXACT_INTERVAL_MS = 9L * 60 * 1000

    /** 没有精确闹钟权限时用 15 分钟，避免 setAndAllowWhileIdle 被系统频繁合并。 */
    const val INEXACT_INTERVAL_MS = 15L * 60 * 1000

    /** 连接/重连状态超过这个时间没有更新，视为卡死。 */
    const val STUCK_CONNECT_MS = 5L * 60 * 1000

    const val ACTION_WATCHDOG = "com.xct258.mqttnotify.action.WATCHDOG"

    /** 用户是否希望持续接收（点「启动接收」为 true，点「停止接收」为 false）。 */
    fun isEnabled(context: Context): Boolean =
        prefs(context).getBoolean(KEY_ENABLED, false)

    fun setEnabled(context: Context, enabled: Boolean) {
        prefs(context).edit()
            .putBoolean(KEY_ENABLED, enabled)
            .apply()

        if (enabled) {
            schedule(context)
        } else {
            cancel(context)
        }
    }

    /** 当前设备可用的自检间隔：精确闹钟权限决定 9 分钟还是 15 分钟。 */
    fun intervalMs(context: Context): Long {
        // 仅 debug 包可调，便于模拟器上压缩等待时间：
        //   adb shell settings put global mqttnotify_watchdog_ms 60000
        if (BuildConfig.DEBUG) {
            val override = Settings.Global.getLong(
                context.contentResolver,
                DEBUG_INTERVAL_SETTING,
                0L,
            )
            if (override in 10_000L..3_600_000L) return override
        }
        return if (canScheduleExactAlarms(context)) EXACT_INTERVAL_MS else INEXACT_INTERVAL_MS
    }

    /** 下一次看门狗还有多久（毫秒）；没有排程时返回 null。 */
    fun nextScheduledInMs(context: Context): Long? {
        val next = prefs(context).getLong(KEY_NEXT_AT, 0L)
        if (next <= 0L) return null
        return (next - SystemClock.elapsedRealtime()).coerceAtLeast(0L)
    }

    /**
     * 确保存在一个未来的看门狗闹钟。
     *
     * 如果已经有闹钟且间隔没有变得“更优”（例如用户刚授予精确闹钟权限，15 分钟可以缩到
     * 9 分钟），就保持原计划，避免每次收到消息都把自检无限推迟。
     */
    fun ensureScheduled(context: Context) {
        if (!isEnabled(context)) return

        val now = SystemClock.elapsedRealtime()
        val next = prefs(context).getLong(KEY_NEXT_AT, 0L)
        val scheduledInterval = prefs(context).getLong(KEY_INTERVAL_MS, 0L)
        val desiredInterval = intervalMs(context)
        val scheduledExact = prefs(context).getBoolean(KEY_SCHEDULED_EXACT, false)
        val canExactNow = canScheduleExactAlarms(context)

        // 只要闹钟还在未来就保留，哪怕只剩 1 秒：否则高频消息会在闹钟临近时
        // 不断重排，把看门狗无限推迟，进程被杀后反而没人拉起。
        val hasFutureAlarm = next > now
        val intervalChanged = scheduledInterval > 0L && scheduledInterval != desiredInterval
        // 撤销精确闹钟权限时系统会删掉已排的精确闹钟，必须重排成非精确版本。
        val exactPermissionChanged = scheduledExact != canExactNow
        if (hasFutureAlarm && !intervalChanged && !exactPermissionChanged) return

        schedule(context)
    }

    /** 排下一次自检（覆盖旧闹钟，保证同一时刻只有一个）。 */
    fun schedule(context: Context) {
        val interval = intervalMs(context)
        // 用 ELAPSED_REALTIME：用户改系统时间不会让闹钟漂移；重启后由 BootReceiver 重排。
        val triggerAt = SystemClock.elapsedRealtime() + interval
        val alarmManager = context.getSystemService(AlarmManager::class.java) ?: return
        val pendingIntent = pendingIntent(context)
        val hasExactPermission = canScheduleExactAlarms(context)

        runCatching {
            when {
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && hasExactPermission ->
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
            prefs(context).edit()
                .putLong(KEY_NEXT_AT, triggerAt)
                .putLong(KEY_INTERVAL_MS, interval)
                .putBoolean(KEY_SCHEDULED_EXACT, hasExactPermission)
                .apply()
            Log.i(
                TAG,
                "看门狗已排程：${interval / 60_000} 分钟后（精确=${hasExactPermission}）",
            )
        }.onFailure { error ->
            Log.w(TAG, "排精确看门狗闹钟失败，降级为非精确闹钟", error)
            runCatching {
                alarmManager.setAndAllowWhileIdle(
                    AlarmManager.ELAPSED_REALTIME_WAKEUP,
                    triggerAt,
                    pendingIntent,
                )
                prefs(context).edit()
                    .putLong(KEY_NEXT_AT, triggerAt)
                    .putLong(KEY_INTERVAL_MS, INEXACT_INTERVAL_MS)
                    .putBoolean(KEY_SCHEDULED_EXACT, false)
                    .apply()
            }.onFailure { Log.w(TAG, "排看门狗闹钟失败", it) }
        }
    }

    fun cancel(context: Context) {
        prefs(context).edit()
            .remove(KEY_NEXT_AT)
            .remove(KEY_INTERVAL_MS)
            .remove(KEY_SCHEDULED_EXACT)
            .apply()
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
        val now = System.currentTimeMillis()
        val statusAge = if (snapshot.changedAt > 0L) now - snapshot.changedAt else Long.MAX_VALUE

        if (snapshot.status == MqttState.Status.CONNECTED && MqttState.isConnectionAlive()) {
            Log.d(TAG, "链路健康，无需干预")
            return
        }

        if (snapshot.status == MqttState.Status.CONNECTED) {
            Log.i(TAG, "已连接但链路假死，主动重连")
            kickOrStart(context, "看门狗：连接假死")
            return
        }

        val stalled = statusAge > STUCK_CONNECT_MS
        if (!stalled &&
            (snapshot.status == MqttState.Status.CONNECTING ||
                snapshot.status == MqttState.Status.RECONNECTING ||
                snapshot.status == MqttState.Status.FAILED)
        ) {
            Log.d(TAG, "服务正在连接/退避（${snapshot.status}），无需干预")
            return
        }

        Log.i(
            TAG,
            "链路不健康（${snapshot.status}，已 ${statusAge / 1000}s 未更新），拉起或重连前台服务",
        )
        kickOrStart(
            context,
            if (stalled) "看门狗：连接状态卡住" else "看门狗：服务未运行",
        )
    }

    /** 服务进程还活着就直接踢一脚重连；进程已经死了才重新拉起。 */
    private fun kickOrStart(context: Context, reason: String) {
        if (MqttForegroundService.forceReconnectIfRunning(reason)) return
        MqttForegroundService.startFromWatchdog(context)
    }

    private fun canScheduleExactAlarms(context: Context): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return true
        val alarmManager = context.getSystemService(AlarmManager::class.java) ?: return false
        return alarmManager.canScheduleExactAlarms()
    }

    private fun prefs(context: Context) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

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
