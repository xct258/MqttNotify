package com.xct258.mqttnotify.ui

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.PowerManager
import android.provider.Settings
import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.xct258.mqttnotify.R
import com.xct258.mqttnotify.data.AppSettings
import com.xct258.mqttnotify.data.MqttTransport
import com.xct258.mqttnotify.mqtt.MqttMessageParser
import com.xct258.mqttnotify.mqtt.MqttState
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AppRoot(viewModel: MainViewModel = viewModel()) {
    val settings by viewModel.settings.collectAsStateWithLifecycle()
    val status by viewModel.status.collectAsStateWithLifecycle()
    val alerts by viewModel.alerts.collectAsStateWithLifecycle()

    // 表单只初始化一次：避免 DataStore 的异步回读覆盖用户正在输入的内容
    var host by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("") }
    var topic by rememberSaveable { mutableStateOf("") }
    var serverName by rememberSaveable { mutableStateOf("") }
    var clientId by rememberSaveable { mutableStateOf("") }
    var autoStart by rememberSaveable { mutableStateOf(true) }
    var transport by rememberSaveable { mutableStateOf(MqttTransport.WSS.name) }
    var wsPath by rememberSaveable { mutableStateOf(AppSettings.DEFAULT_WS_PATH) }
    var insecureTls by rememberSaveable { mutableStateOf(false) }
    var formLoaded by rememberSaveable { mutableStateOf(false) }
    var showSettings by rememberSaveable { mutableStateOf(false) }

    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    var needsBatteryExemption by remember { mutableStateOf(!isIgnoringBatteryOptimizations(context)) }
    var exactAlarmGranted by remember {
        mutableStateOf(KeepAliveSettings.canScheduleExactAlarms(context))
    }
    var notificationsEnabled by remember {
        mutableStateOf(KeepAliveSettings.areNotificationsEnabled(context))
    }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) {
        needsBatteryExemption = !isIgnoringBatteryOptimizations(context)
        exactAlarmGranted = KeepAliveSettings.canScheduleExactAlarms(context)
        notificationsEnabled = KeepAliveSettings.areNotificationsEnabled(context)
    }

    LaunchedEffect(settings) {
        if (!formLoaded) {
            host = settings.host
            port = settings.port.toString()
            topic = settings.topicBase
            serverName = settings.serverName
            clientId = settings.clientId
            autoStart = settings.autoStartOnBoot
            transport = settings.transport.name
            wsPath = settings.effectiveWsPath
            insecureTls = settings.allowInsecureTls
            formLoaded = true
        }
    }

    val portValue = port.toIntOrNull()
    val canSave = host.isNotBlank() && topic.isNotBlank() && portValue != null && portValue in 1..65535
    val transportValue = remember(transport) {
        runCatching { MqttTransport.valueOf(transport) }.getOrDefault(MqttTransport.WSS)
    }
    val running = status.status != MqttState.Status.STOPPED
    val savedMessage = stringResource(R.string.ui_saved)

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    if (showSettings) {
                        Text(stringResource(R.string.ui_settings_title))
                    } else {
                        Column {
                            Text(
                                text = stringResource(R.string.app_name),
                                style = MaterialTheme.typography.titleMedium,
                                fontWeight = FontWeight.SemiBold,
                            )
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Box(
                                    modifier = Modifier
                                        .size(6.dp)
                                        .background(statusColor(status.status), CircleShape),
                                )
                                Text(
                                    text = stringResource(statusLabelRes(status.status)),
                                    fontSize = 11.sp,
                                    color = statusColor(status.status),
                                    modifier = Modifier.padding(start = 6.dp),
                                )
                            }
                        }
                    }
                },
                navigationIcon = {
                    if (showSettings) {
                        IconButton(onClick = { showSettings = false }) {
                            Icon(
                                imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                                contentDescription = "返回",
                            )
                        }
                    }
                },
                actions = {
                    if (!showSettings) {
                        IconButton(onClick = { showSettings = true }) {
                            Icon(
                                imageVector = Icons.Filled.Settings,
                                contentDescription = stringResource(R.string.ui_settings_title),
                            )
                        }
                    }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            if (!showSettings && !running) {
                item {
                    Text(
                        text = "接收已停止，请在右上角设置中启动",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }

            if (showSettings) {
                item {
                Card {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Text(
                        text = "连接操作",
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Button(
                            onClick = { viewModel.startReceiving() },
                            enabled = !running && settings.isValid,
                            modifier = Modifier.weight(1f),
                        ) { Text(stringResource(R.string.ui_start)) }
                        OutlinedButton(
                            onClick = { viewModel.stopReceiving() },
                            enabled = running,
                            modifier = Modifier.weight(1f),
                        ) { Text(stringResource(R.string.ui_stop)) }
                    }
                    Text(
                        text = "启动/停止只影响当前连接，不会修改下面的配置。",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    HorizontalDivider()

                    Text(
                        text = stringResource(R.string.ui_transport),
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )

                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        MqttTransport.entries.forEach { option ->
                            FilterChip(
                                selected = transportValue == option,
                                onClick = {
                                    // 端口还是旧方式的默认值时才跟随切换，避免覆盖用户自定义端口
                                    if (port.isBlank() || port.toIntOrNull() == transportValue.defaultPort) {
                                        port = option.defaultPort.toString()
                                    }
                                    transport = option.name
                                },
                                label = { Text(option.scheme.uppercase()) },
                            )
                        }
                    }

                    Text(
                        text = transportValue.displayUrl(
                            host,
                            portValue ?: transportValue.defaultPort,
                            wsPath,
                        ),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )

                    OutlinedTextField(
                        value = host,
                        onValueChange = { host = it },
                        label = { Text(stringResource(R.string.ui_host)) },
                        placeholder = { Text(stringResource(R.string.ui_host_hint)) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )

                    OutlinedTextField(
                        value = port,
                        onValueChange = { input -> port = input.filter { it.isDigit() }.take(5) },
                        label = { Text(stringResource(R.string.ui_port)) },
                        singleLine = true,
                        isError = port.isNotBlank() && portValue == null,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.fillMaxWidth(),
                    )

                    if (transportValue.isWebSocket) {
                        OutlinedTextField(
                            value = wsPath,
                            onValueChange = { wsPath = it },
                            label = { Text(stringResource(R.string.ui_ws_path)) },
                            placeholder = { Text(stringResource(R.string.ui_ws_path_hint)) },
                            singleLine = true,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }

                    if (transportValue.isTls) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text(
                                text = stringResource(R.string.ui_allow_insecure_tls),
                                modifier = Modifier.weight(1f),
                                style = MaterialTheme.typography.bodyLarge,
                            )
                            Switch(checked = insecureTls, onCheckedChange = { insecureTls = it })
                        }
                        Text(
                            text = stringResource(R.string.ui_insecure_tls_warning),
                            style = MaterialTheme.typography.bodySmall,
                            color = if (insecureTls) {
                                MaterialTheme.colorScheme.error
                            } else {
                                MaterialTheme.colorScheme.onSurfaceVariant
                            },
                        )
                    }

                    OutlinedTextField(
                        value = topic,
                        onValueChange = { topic = it },
                        label = { Text(stringResource(R.string.ui_topic)) },
                        placeholder = { Text(stringResource(R.string.ui_topic_hint)) },
                        singleLine = true,
                        supportingText = { Text(settings.subscribeTopic) },
                        modifier = Modifier.fillMaxWidth(),
                    )

                    OutlinedTextField(
                        value = serverName,
                        onValueChange = { serverName = it },
                        label = { Text(stringResource(R.string.ui_server_name)) },
                        placeholder = { Text(stringResource(R.string.ui_server_name_hint)) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )

                    OutlinedTextField(
                        value = clientId,
                        onValueChange = { clientId = it },
                        label = { Text(stringResource(R.string.ui_client_id)) },
                        placeholder = { Text(stringResource(R.string.ui_client_id_hint)) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(
                            text = stringResource(R.string.ui_auto_start),
                            modifier = Modifier.weight(1f),
                            style = MaterialTheme.typography.bodyLarge,
                        )
                        Switch(checked = autoStart, onCheckedChange = { autoStart = it })
                    }

                    HorizontalDivider()

                    Text(
                        text = "后台保活",
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Text(
                        text = stringResource(R.string.ui_keepalive_hint),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )

                    KeepAliveRow(
                        title = stringResource(R.string.ui_battery_optimization),
                        hint = stringResource(R.string.ui_battery_optimization_hint),
                        actionLabel = stringResource(
                            if (needsBatteryExemption) R.string.ui_battery_optimization_action
                            else R.string.ui_battery_optimization_done
                        ),
                        actionEnabled = needsBatteryExemption,
                        onClick = { requestIgnoreBatteryOptimizations(context) },
                    )

                    HorizontalDivider()
                    KeepAliveRow(
                        title = stringResource(R.string.ui_notifications),
                        hint = stringResource(R.string.ui_notifications_hint),
                        actionLabel = stringResource(
                            if (notificationsEnabled) R.string.ui_notifications_done
                            else R.string.ui_notifications_action
                        ),
                        actionEnabled = !notificationsEnabled,
                        onClick = { KeepAliveSettings.openNotificationSettings(context) },
                    )

                    if (KeepAliveSettings.supportsExactAlarms()) {
                        HorizontalDivider()
                        KeepAliveRow(
                            title = stringResource(R.string.ui_exact_alarm),
                            hint = stringResource(R.string.ui_exact_alarm_hint),
                            actionLabel = stringResource(
                                if (exactAlarmGranted) R.string.ui_exact_alarm_done
                                else R.string.ui_exact_alarm_action
                            ),
                            actionEnabled = !exactAlarmGranted,
                            onClick = { KeepAliveSettings.requestExactAlarmPermission(context) },
                        )
                    }

                    HorizontalDivider()
                    KeepAliveRow(
                        title = stringResource(R.string.ui_autostart),
                        hint = stringResource(R.string.ui_autostart_hint),
                        actionLabel = stringResource(R.string.ui_autostart_action),
                        onClick = { KeepAliveSettings.openAutoStartSettings(context) },
                    )

                    Button(
                        onClick = {
                            viewModel.save(
                                AppSettings(
                                    host = host,
                                    port = portValue ?: AppSettings.DEFAULT_PORT,
                                    topicBase = topic,
                                    serverName = serverName,
                                    clientId = clientId,
                                    autoStartOnBoot = autoStart,
                                    transport = transportValue,
                                    wsPath = wsPath,
                                    allowInsecureTls = insecureTls,
                                )
                            )
                            scope.launch { snackbarHostState.showSnackbar(savedMessage) }
                        },
                        enabled = canSave,
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text(stringResource(R.string.ui_save)) }

                    if (!settings.isValid) {
                        Text(
                            text = stringResource(R.string.ui_host_hint),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                }
            }
            }
            }

            if (!showSettings) {
                item {
                    Text(
                        text = stringResource(R.string.ui_recent),
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                    )
                }

                if (alerts.isEmpty()) {
                    item {
                        Text(
                            text = stringResource(R.string.ui_no_alerts),
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                } else {
                    items(alerts, key = { it.id }) { alert ->
                        AlertCard(alert)
                    }
                }
            }
        }
    }
}

@Composable
private fun statusColor(status: MqttState.Status): Color = when (status) {
    MqttState.Status.CONNECTED -> MaterialTheme.colorScheme.primary
    MqttState.Status.FAILED -> MaterialTheme.colorScheme.error
    MqttState.Status.STOPPED -> MaterialTheme.colorScheme.onSurfaceVariant
    else -> MaterialTheme.colorScheme.tertiary
}

@Composable
private fun KeepAliveRow(
    title: String,
    hint: String,
    actionLabel: String,
    actionEnabled: Boolean = true,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyLarge,
            )
            Text(
                text = hint,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Button(
            onClick = onClick,
            enabled = actionEnabled,
        ) {
            Text(actionLabel)
        }
    }
}

@Composable
private fun AlertCard(alert: MqttState.ReceivedAlert) {
    when (alert.kind) {
        MqttMessageParser.Kind.SERVICE -> ServiceAlertCard(alert)
        else -> StandardAlertCard(alert)
    }
}

@Composable
private fun StandardAlertCard(alert: MqttState.ReceivedAlert) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Text(
                text = alert.title,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )
            if (alert.body.isNotBlank()) {
                MarkdownText(
                    markdown = alert.body,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            AlertFooter(alert)
        }
    }
}

/**
 * 服务通知卡片：
 *   o1-debian-1                    [OK]
 *   录播备份上传脚本
 *   ────────────────────────────
 *   上传成功（完整 Markdown）
 *   17:30:10 · server/khx-live
 */
@Composable
private fun ServiceAlertCard(alert: MqttState.ReceivedAlert) {
    val title = alert.serverName ?: alert.title
    val statusStyle = statusStyle(alert.status)

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = title,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                )
                statusStyle?.let { StatusBadge(it) }
            }

            if (!alert.serviceName.isNullOrBlank()) {
                Text(
                    text = alert.serviceName,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }

            HorizontalDivider(modifier = Modifier.padding(vertical = 8.dp))

            if (alert.body.isBlank()) {
                Text(
                    text = "（无内容）",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                MarkdownText(
                    markdown = alert.body,
                    modifier = Modifier.fillMaxWidth(),
                )
            }

            AlertFooter(alert)
        }
    }
}

@Composable
private fun AlertFooter(alert: MqttState.ReceivedAlert) {
    Text(
        text = buildString {
            append(formatTime(alert.receivedAt))
            append(" · ")
            append(alert.topic)
            alert.taskId?.let { append(" · #$it") }
        },
        style = MaterialTheme.typography.labelSmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
}

private data class StatusStyle(val label: String, val color: Color)

private fun statusStyle(status: String?): StatusStyle? = when (status?.trim()?.lowercase()) {
    "ok" -> StatusStyle("OK", Color(0xFF22C55E))
    "warn" -> StatusStyle("WARN", Color(0xFFF59E0B))
    "error" -> StatusStyle("ERROR", Color(0xFFEF4444))
    "info" -> StatusStyle("INFO", Color(0xFF38BDF8))
    else -> null
}

@Composable
private fun StatusBadge(style: StatusStyle) {
    Text(
        text = style.label,
        style = MaterialTheme.typography.labelSmall,
        color = style.color,
        fontWeight = FontWeight.Bold,
        modifier = Modifier
            .background(style.color.copy(alpha = 0.12f), RoundedCornerShape(6.dp))
            .padding(horizontal = 6.dp, vertical = 2.dp),
    )
}

@StringRes
private fun statusLabelRes(status: MqttState.Status): Int = when (status) {
    MqttState.Status.STOPPED -> R.string.status_stopped
    MqttState.Status.CONNECTING -> R.string.status_connecting
    MqttState.Status.CONNECTED -> R.string.status_connected
    MqttState.Status.RECONNECTING -> R.string.status_reconnecting
    MqttState.Status.FAILED -> R.string.status_failed
}

private fun formatTime(millis: Long): String =
    SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date(millis))

private fun isIgnoringBatteryOptimizations(context: Context): Boolean {
    val powerManager = context.getSystemService(PowerManager::class.java) ?: return false
    return powerManager.isIgnoringBatteryOptimizations(context.packageName)
}

private fun requestIgnoreBatteryOptimizations(context: Context) {
    if (isIgnoringBatteryOptimizations(context)) return
    val request = Intent(
        Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,
        Uri.parse("package:${context.packageName}"),
    )
    runCatching { context.startActivity(request) }.onFailure {
        runCatching {
            context.startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
        }
    }
}
