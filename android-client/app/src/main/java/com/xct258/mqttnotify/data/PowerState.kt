package com.xct258.mqttnotify.data

import android.content.Context
import android.net.ConnectivityManager
import android.os.Build
import android.os.PowerManager

/**
 * 系统级省电/限制状态。
 *
 * Android 13+ 的 Low Power Standby（部分 OEM 把「超级省电」映射到它）、
 * 普通省电模式、Doze/Light Doze、后台数据限制都会让长连接收不到消息。
 * 进入这些状态时不做无意义的重连；退出时由服务里的运行时接收器立即重建连接。
 */
object PowerState {

    fun isPowerSaveMode(context: Context): Boolean =
        context.getSystemService(PowerManager::class.java)?.isPowerSaveMode == true

    fun isLowPowerStandby(context: Context): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return false
        return context.getSystemService(PowerManager::class.java)
            ?.isLowPowerStandbyEnabled == true
    }

    fun isDeviceIdle(context: Context): Boolean =
        context.getSystemService(PowerManager::class.java)?.isDeviceIdleMode == true

    fun isBackgroundDataRestricted(context: Context): Boolean {
        val manager = context.getSystemService(ConnectivityManager::class.java) ?: return false
        return manager.restrictBackgroundStatus ==
            ConnectivityManager.RESTRICT_BACKGROUND_STATUS_ENABLED
    }

    /** 是否处于任何可能掐断后台长连接的限制中。 */
    fun isRestricted(context: Context): Boolean =
        isPowerSaveMode(context) ||
            isLowPowerStandby(context) ||
            isDeviceIdle(context) ||
            isBackgroundDataRestricted(context)

    /** 用户能在系统设置里直接看到的省电模式（Doze 是自动进入的，不在此列）。 */
    fun isUserVisibleRestriction(context: Context): Boolean =
        isPowerSaveMode(context) || isLowPowerStandby(context)
}
