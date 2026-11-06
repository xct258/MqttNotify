package com.xct258.mqttnotify.ui

import android.app.AlarmManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.app.NotificationManagerCompat

/**
 * 「后台保活」相关的系统设置跳转。
 *
 * 不同 ROM 的自启动/后台管理页面组件名不一样，这里按常见厂商依次尝试，
 * 全部失败再落到系统应用详情页。调用方直接 startActivity，不依赖
 * PackageManager 查询（避免 Android 11+ 包可见性影响）。
 */
object KeepAliveSettings {

    fun supportsExactAlarms(): Boolean = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S

    fun canScheduleExactAlarms(context: Context): Boolean {
        if (!supportsExactAlarms()) return true
        val alarmManager = context.getSystemService(AlarmManager::class.java) ?: return false
        return alarmManager.canScheduleExactAlarms()
    }

    fun areNotificationsEnabled(context: Context): Boolean =
        NotificationManagerCompat.from(context).areNotificationsEnabled()

    fun openNotificationSettings(context: Context) {
        val intent = Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
            .putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
            .onFailure { openAppDetails(context) }
    }

    fun requestExactAlarmPermission(context: Context) {
        if (!supportsExactAlarms()) return
        val request = Intent(
            Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
            Uri.parse("package:${context.packageName}"),
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(request) }
            .onFailure { openAppDetails(context) }
    }

    fun openAutoStartSettings(context: Context) {
        for ((packageName, className) in AUTO_START_ACTIVITIES) {
            val intent = Intent()
                .setComponent(ComponentName(packageName, className))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            if (runCatching { context.startActivity(intent) }.isSuccess) return
        }
        openAppDetails(context)
    }

    fun openAppDetails(context: Context) {
        val intent = Intent(
            Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
            Uri.parse("package:${context.packageName}"),
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
    }

    private val AUTO_START_ACTIVITIES = listOf(
        // MIUI
        "com.miui.securitycenter" to
            "com.miui.permcenter.autostart.AutoStartManagementActivity",
        // EMUI / HarmonyOS
        "com.huawei.systemmanager" to
            "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity",
        "com.huawei.systemmanager" to
            "com.huawei.systemmanager.optimize.process.ProtectActivity",
        // ColorOS / realme UI
        "com.coloros.safecenter" to
            "com.coloros.safecenter.permission.startup.StartupAppListActivity",
        "com.oppo.safe" to
            "com.oppo.safe.permission.startup.StartupAppListActivity",
        // OriginOS / iQOO
        "com.vivo.permissionmanager" to
            "com.vivo.permissionmanager.activity.BgStartUpManagerActivity",
        "com.iqoo.secure" to
            "com.iqoo.secure.ui.phoneoptimize.AddWhiteListActivity",
        // 其他
        "com.letv.android.letvsafe" to
            "com.letv.android.letvsafe.AutobootManageActivity",
        "com.samsung.android.lool" to
            "com.samsung.android.sm.ui.battery.BatteryActivity",
    )
}
