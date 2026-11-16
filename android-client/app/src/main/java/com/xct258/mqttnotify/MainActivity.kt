package com.xct258.mqttnotify

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import com.xct258.mqttnotify.mqtt.MqttState
import com.xct258.mqttnotify.service.MqttForegroundService
import com.xct258.mqttnotify.service.MqttWatchdog
import com.xct258.mqttnotify.ui.AppRoot
import com.xct258.mqttnotify.ui.theme.MqttNotifyTheme

class MainActivity : ComponentActivity() {

    private val notificationPermissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { /* 结果由系统状态决定，无需额外处理 */ }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        requestNotificationPermissionIfNeeded()
        setContent {
            MqttNotifyTheme {
                AppRoot()
            }
        }
    }

    override fun onResume() {
        super.onResume()
        // 打开 App 是最可靠的“自愈”时机：如果用户之前开着接收、但进程/闹钟被 ROM
        // 清掉了，这里立刻把前台服务和看门狗补回来，不必等下一次系统维护窗口。
        if (MqttWatchdog.isEnabled(this) &&
            MqttState.status.value.status == MqttState.Status.STOPPED
        ) {
            MqttForegroundService.start(this)
        } else if (MqttWatchdog.isEnabled(this)) {
            MqttWatchdog.ensureScheduled(this)
        }
    }

    /** Android 13+ 未授权时请求通知权限（未授权只影响弹通知，不影响连接）。 */
    private fun requestNotificationPermissionIfNeeded() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return
        val granted = ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED
        if (!granted) {
            notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }
}
