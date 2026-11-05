package com.xct258.mqttnotify.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.map
import java.io.IOException
import java.util.UUID

private val Context.settingsDataStore: DataStore<Preferences> by preferencesDataStore(name = "mqtt_settings")

/**
 * 连接设置的持久化仓库（DataStore Preferences）。
 *
 * 服务与 UI 共用同一份配置：服务 collect [settings] 后按变化重连，
 * UI 保存后无需手动通知服务。
 */
class SettingsRepository private constructor(
    private val dataStore: DataStore<Preferences>,
    private val clientIdSuffix: String,
) {

    val settings: Flow<AppSettings> = dataStore.data
        .catch { e ->
            // 读取损坏时降级为空配置，不崩溃
            if (e is IOException) emit(emptyPreferences()) else throw e
        }
        .map { prefs ->
            // 开发期不做迁移：旧版本的配置直接忽略，回落到当前默认值。
            if ((prefs[KEY_CONFIG_VERSION] ?: 0) != AppSettings.CONFIG_VERSION) {
                AppSettings(clientIdSuffix = clientIdSuffix)
            } else {
                AppSettings(
                    host = prefs[KEY_HOST] ?: AppSettings.DEFAULT_HOST,
                    port = prefs[KEY_PORT] ?: AppSettings.DEFAULT_PORT,
                    topicBase = prefs[KEY_TOPIC] ?: AppSettings.DEFAULT_TOPIC,
                    serverName = prefs[KEY_SERVER] ?: AppSettings.DEFAULT_SERVER,
                    clientId = prefs[KEY_CLIENT_ID] ?: "",
                    clientIdSuffix = clientIdSuffix,
                    autoStartOnBoot = prefs[KEY_AUTO_START] ?: true,
                    transport = prefs[KEY_TRANSPORT]
                        ?.let { name -> runCatching { MqttTransport.valueOf(name) }.getOrNull() }
                        ?: AppSettings.DEFAULT_TRANSPORT,
                    wsPath = prefs[KEY_WS_PATH] ?: AppSettings.DEFAULT_WS_PATH,
                    allowInsecureTls = prefs[KEY_INSECURE_TLS] ?: false,
                )
            }
        }

    suspend fun save(settings: AppSettings) {
        val s = settings.normalized()
        dataStore.edit { prefs ->
            prefs[KEY_CONFIG_VERSION] = AppSettings.CONFIG_VERSION
            prefs[KEY_HOST] = s.host
            prefs[KEY_PORT] = s.port
            prefs[KEY_TOPIC] = s.topicBase
            prefs[KEY_SERVER] = s.serverName
            prefs[KEY_CLIENT_ID] = s.clientId
            prefs[KEY_AUTO_START] = s.autoStartOnBoot
            prefs[KEY_TRANSPORT] = s.transport.name
            prefs[KEY_WS_PATH] = s.wsPath
            prefs[KEY_INSECURE_TLS] = s.allowInsecureTls
        }
    }

    companion object {
        @Volatile
        private var instance: SettingsRepository? = null

        fun get(context: Context): SettingsRepository =
            instance ?: synchronized(this) {
                instance ?: SettingsRepository(
                    context.applicationContext.settingsDataStore,
                    installClientIdSuffix(context.applicationContext),
                ).also { instance = it }
            }

        /** 随机后缀保存在独立的 SharedPreferences 中：清空 App 数据/重装才会重新生成。 */
        private fun installClientIdSuffix(context: Context): String {
            val prefs = context.getSharedPreferences(IDENTITY_PREFS, Context.MODE_PRIVATE)
            prefs.getString(KEY_IDENTITY_SUFFIX, null)
                ?.takeIf { it.isNotBlank() }
                ?.let { return it }

            val suffix = UUID.randomUUID().toString().replace("-", "").take(8)
            prefs.edit().putString(KEY_IDENTITY_SUFFIX, suffix).apply()
            return suffix
        }

        private const val IDENTITY_PREFS = "mqtt_identity"
        private const val KEY_IDENTITY_SUFFIX = "client_id_suffix"

        private val KEY_CONFIG_VERSION = intPreferencesKey("config_version")
        private val KEY_HOST = stringPreferencesKey("host")
        private val KEY_PORT = intPreferencesKey("port")
        private val KEY_TOPIC = stringPreferencesKey("topic_base")
        private val KEY_SERVER = stringPreferencesKey("server_name")
        private val KEY_CLIENT_ID = stringPreferencesKey("client_id")
        private val KEY_AUTO_START = booleanPreferencesKey("auto_start_on_boot")
        private val KEY_TRANSPORT = stringPreferencesKey("transport")
        private val KEY_WS_PATH = stringPreferencesKey("ws_path")
        private val KEY_INSECURE_TLS = booleanPreferencesKey("allow_insecure_tls")
    }
}
