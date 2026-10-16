package com.xct258.mqttnotify.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.xct258.mqttnotify.data.AppSettings
import com.xct258.mqttnotify.data.SettingsRepository
import com.xct258.mqttnotify.mqtt.MqttState
import com.xct258.mqttnotify.service.MqttForegroundService
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

class MainViewModel(application: Application) : AndroidViewModel(application) {

    private val repository = SettingsRepository.get(application)

    val settings: StateFlow<AppSettings> = repository.settings
        .stateIn(viewModelScope, SharingStarted.Eagerly, AppSettings())

    val status: StateFlow<MqttState.Snapshot> = MqttState.status
    val alerts: StateFlow<List<MqttState.ReceivedAlert>> = MqttState.alerts

    fun save(settings: AppSettings) {
        viewModelScope.launch { repository.save(settings) }
    }

    fun startReceiving() = MqttForegroundService.start(getApplication<Application>())

    fun stopReceiving() = MqttForegroundService.stop(getApplication<Application>())
}
