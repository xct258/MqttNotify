package com.xct258.mqttnotify.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val Blue = Color(0xFF1B5E9B)
private val BlueDark = Color(0xFF8EC6F5)

private val LightColors = lightColorScheme(
    primary = Blue,
    secondary = Color(0xFF4A6572),
)

private val DarkColors = darkColorScheme(
    primary = BlueDark,
    secondary = Color(0xFFB8CBD6),
)

@Composable
fun MqttNotifyTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        content = content,
    )
}
