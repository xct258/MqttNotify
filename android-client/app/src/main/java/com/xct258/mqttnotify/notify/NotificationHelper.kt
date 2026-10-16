package com.xct258.mqttnotify.notify

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.xct258.mqttnotify.MainActivity
import com.xct258.mqttnotify.R
import com.xct258.mqttnotify.mqtt.MqttState
import java.util.concurrent.atomic.AtomicInteger

/**
 * 通知构建与发送。
 *
 * 两个渠道：
 *  - [CHANNEL_SERVICE]：低优先级常驻通知，展示连接状态；
 *  - [CHANNEL_ALERTS]：高优先级，提醒到达时弹窗。
 */
object NotificationHelper {

    const val CHANNEL_SERVICE = "mqtt_service"
    const val CHANNEL_ALERTS = "mqtt_alerts"

    private const val GROUP_ALERTS = "com.xct258.mqttnotify.ALERTS"

    /** 提醒通知 ID 自增，避免新提醒覆盖旧提醒。 */
    private val alertId = AtomicInteger(2000)

    fun createChannels(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java) ?: return

        val serviceChannel = NotificationChannel(
            CHANNEL_SERVICE,
            context.getString(R.string.channel_service_name),
            NotificationManager.IMPORTANCE_LOW,
        ).apply {
            description = context.getString(R.string.channel_service_desc)
            setShowBadge(false)
        }

        val alertChannel = NotificationChannel(
            CHANNEL_ALERTS,
            context.getString(R.string.channel_alerts_name),
            NotificationManager.IMPORTANCE_HIGH,
        ).apply {
            description = context.getString(R.string.channel_alerts_desc)
            enableVibration(true)
        }

        manager.createNotificationChannels(listOf(serviceChannel, alertChannel))
    }

    /** 常驻前台服务通知。 */
    fun serviceNotification(context: Context, statusLabel: String, detail: String): Notification {
        val text = if (detail.isBlank()) statusLabel else "$statusLabel · $detail"
        return NotificationCompat.Builder(context, CHANNEL_SERVICE)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(context.getString(R.string.app_name))
            .setContentText(text)
            .setContentIntent(contentIntent(context, requestCode = 0))
            .setOngoing(true)
            .setShowWhen(false)
            .setOnlyAlertOnce(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .build()
    }

    /** 待办提醒通知。 */
    fun notifyAlert(context: Context, alert: MqttState.ReceivedAlert) {
        if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) return

        val body = alert.body.ifBlank { alert.title }
        val builder = NotificationCompat.Builder(context, CHANNEL_ALERTS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(alert.title)
            .setContentText(body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(body))
            .setSubText(alert.topic)
            .setContentIntent(contentIntent(context, alert.receivedAt.toInt()))
            .setAutoCancel(true)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setGroup(GROUP_ALERTS)

        // Android 13+ 未授权时会抛 SecurityException，静默忽略（UI 已单独提示授权）。
        runCatching {
            NotificationManagerCompat.from(context).notify(alertId.incrementAndGet(), builder.build())
        }
    }

    private fun contentIntent(context: Context, requestCode: Int): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        return PendingIntent.getActivity(
            context,
            requestCode,
            intent,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
    }
}
