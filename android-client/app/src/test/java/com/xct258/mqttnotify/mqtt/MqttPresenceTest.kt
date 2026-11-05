package com.xct258.mqttnotify.mqtt

import org.junit.Assert.assertEquals
import org.junit.Test

class MqttPresenceTest {

    @Test
    fun `在线状态主题会清理客户端 ID 里的斜杠`() {
        assertEquals(
            "\$presence/clients/android-a_b",
            MqttPresence.topicFor("android-a/b"),
        )
    }
}
