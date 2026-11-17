package com.xct258.mqttnotify.ui

import android.app.AlarmManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.app.NotificationManagerCompat
import com.xct258.mqttnotify.data.PowerState
import java.util.Locale

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

    /** 用户可见的省电模式/低功耗待机（Doze 是系统自动的，不在这里提示）。 */
    fun isPowerRestricted(context: Context): Boolean =
        PowerState.isUserVisibleRestriction(context)

    /** 荣耀 MagicOS 5+/8+ 的省电/超级省电白名单入口与华为不同。 */
    fun isHonor(): Boolean {
        val name = "${Build.BRAND} ${Build.MANUFACTURER}".lowercase(Locale.ROOT)
        return name.contains("honor")
    }

    fun openBatterySaverSettings(context: Context) {
        // 荣耀的超级省电白名单在系统管家的 preventmode 页面，优先尝试。
        if (isHonor() && openFirst(context, HONOR_POWER_ROUTES)) return

        val intent = Intent(Settings.ACTION_BATTERY_SAVER_SETTINGS)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
            .onFailure { openAppDetails(context) }
    }

    /** 荣耀「超级省电 → 添加常用应用」和电池使用模式页面。 */
    fun openHonorSuperSavingSettings(context: Context) {
        if (openFirst(context, HONOR_SUPER_SAVING_ROUTES)) return
        if (openFirst(context, HONOR_POWER_ROUTES)) return
        openBatterySaverSettings(context)
    }

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
        if (openFirst(context, AUTO_START_ACTIVITIES)) return
        openAppDetails(context)
    }

    private fun openFirst(context: Context, routes: List<Pair<String, String>>): Boolean {
        for ((packageName, className) in routes) {
            val intent = Intent()
                .setComponent(ComponentName(packageName, className))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            if (runCatching { context.startActivity(intent) }.isSuccess) return true
        }
        return false
    }

    fun openAppDetails(context: Context) {
        val intent = Intent(
            Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
            Uri.parse("package:${context.packageName}"),
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
    }

    private val AUTO_START_ACTIVITIES = listOf(
        // 荣耀 MagicOS 8+（独立后的包名，优先于华为）
        "com.hihonor.systemmanager" to
            "com.hihonor.systemmanager.startupmgr.ui.StartupNormalAppListActivity",
        "com.hihonor.systemmanager" to
            "com.hihonor.systemmanager.appcontrol.activity.StartupAppControlActivity",
        "com.hihonor.systemmanager" to
            "com.hihonor.systemmanager.optimize.process.ProtectActivity",
        // 旧荣耀 / EMUI / HarmonyOS
        "com.huawei.systemmanager" to
            "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity",
        "com.huawei.systemmanager" to
            "com.huawei.systemmanager.appcontrol.activity.StartupAppControlActivity",
        "com.huawei.systemmanager" to
            "com.huawei.systemmanager.optimize.process.ProtectActivity",
        // MIUI
        "com.miui.securitycenter" to
            "com.miui.permcenter.autostart.AutoStartManagementActivity",
        // ColorOS / realme UI
        "com.coloros.safecenter" to
            "com.coloros.safecenter.permission.startup.StartupAppListActivity",
        "com.oppo.safe" to
            "com.oppo.safe.permission.startup.StartupAppListActivity",
        "com.coloros.oppoguardelf" to
            "com.coloros.powermanager.fuelgaue.PowerUsageModelActivity",
        // OriginOS / iQOO
        "com.vivo.permissionmanager" to
            "com.vivo.permissionmanager.activity.BgStartUpManagerActivity",
        "com.iqoo.secure" to
            "com.iqoo.secure.ui.phoneoptimize.AddWhiteListActivity",
        "com.iqoo.secure" to
            "com.iqoo.secure.ui.phoneoptimize.BgStartUpManager",
        // 其他
        "com.letv.android.letvsafe" to
            "com.letv.android.letvsafe.AutobootManageActivity",
        "com.samsung.android.lool" to
            "com.samsung.android.sm.ui.battery.BatteryActivity",
        "com.samsung.android.sm" to
            "com.samsung.android.sm.app.dashboard.SmartManagerDashBoardActivity",
        "com.meizu.safe" to
            "com.meizu.safe.permission.SmartBGActivity",
        "com.oneplus.security" to
            "com.oneplus.security.chainlaunch.view.ChainLaunchAppListActivity",
    )

    private val HONOR_SUPER_SAVING_ROUTES = listOf(
        // 超级省电的“添加常用应用”白名单
        "com.hihonor.systemmanager" to
            "com.hihonor.systemmanager.preventmode.WhiteNameActivity",
        "com.hihonor.systemmanager" to
            "com.hihonor.systemmanager.preventmode.PreventModeActivity",
    )

    private val HONOR_POWER_ROUTES = listOf(
        "com.hihonor.systemmanager" to
            "com.hihonor.systemmanager.power.ui.HwPowerManagerActivity",
        "com.hihonor.systemmanager" to
            "com.hihonor.systemmanager.power.ui.PowerSettingActivity",
    )
}
