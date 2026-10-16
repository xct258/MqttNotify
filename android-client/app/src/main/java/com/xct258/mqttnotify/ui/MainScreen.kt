package com.xct258.mqttnotify.ui

import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
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
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.xct258.mqttnotify.R
import com.xct258.mqttnotify.data.AppSettings
import com.xct258.mqttnotify.data.MqttTransport
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
    var transport by rememberSaveable { mutableStateOf(MqttTransport.TCP.name) }
    var wsPath by rememberSaveable { mutableStateOf(AppSettings.DEFAULT_WS_PATH) }
    var insecureTls by rememberSaveable { mutableStateOf(false) }
    var formLoaded by rememberSaveable { mutableStateOf(false) }

    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

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
        runCatching { MqttTransport.valueOf(transport) }.getOrDefault(MqttTransport.TCP)
    }
    val running = status.status != MqttState.Status.STOPPED
    val savedMessage = stringResource(R.string.ui_saved)

    Scaffold(
        topBar = { TopAppBar(title = { Text(stringResource(R.string.app_name)) }) },
        snackbarHost = { SnackbarHost(snackbarHostState) },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            StatusCard(status)

            if (running) {
                Button(
                    onClick = { viewModel.stopReceiving() },
                    modifier = Modifier.fillMaxWidth(),
                ) { Text(stringResource(R.string.ui_stop)) }
            } else {
                Button(
                    onClick = { viewModel.startReceiving() },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = settings.isValid,
                ) { Text(stringResource(R.string.ui_start)) }
            }

            Card {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Text(
                        text = stringResource(R.string.ui_settings_title),
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                    )

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

            HorizontalDivider()

            Text(
                text = stringResource(R.string.ui_recent),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )

            if (alerts.isEmpty()) {
                Text(
                    text = stringResource(R.string.ui_no_alerts),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                alerts.forEach { alert -> AlertCard(alert) }
            }
        }
    }
}

@Composable
private fun StatusCard(status: MqttState.Snapshot) {
    val statusColor = when (status.status) {
        MqttState.Status.CONNECTED -> MaterialTheme.colorScheme.primary
        MqttState.Status.FAILED -> MaterialTheme.colorScheme.error
        MqttState.Status.STOPPED -> MaterialTheme.colorScheme.onSurfaceVariant
        else -> MaterialTheme.colorScheme.tertiary
    }

    Card {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Text(
                text = stringResource(R.string.ui_status_title),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Text(
                text = stringResource(statusLabelRes(status.status)),
                style = MaterialTheme.typography.headlineSmall,
                color = statusColor,
                fontWeight = FontWeight.Bold,
            )
            if (status.detail.isNotBlank()) {
                Text(
                    text = status.detail,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun AlertCard(alert: MqttState.ReceivedAlert) {
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
                Text(text = alert.body, style = MaterialTheme.typography.bodyMedium)
            }
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
    }
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
