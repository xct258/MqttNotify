package com.xct258.mqttnotify.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AppSettingsTest {

    @Test
    fun `默认与 push-server 文档一致`() {
        val s = AppSettings()
        assertEquals(1883, s.port)
        assertEquals("taskops/todo", s.topicBase)
        assertEquals("taskops/todo/#", s.subscribeTopic)
    }

    @Test
    fun `订阅主题拼接通配符并去掉多余斜杠`() {
        assertEquals("a/b/#", AppSettings(topicBase = "a/b").subscribeTopic)
        assertEquals("a/b/#", AppSettings(topicBase = "a/b/").subscribeTopic)
        assertEquals("a/b/#", AppSettings(topicBase = "  a/b//  ").subscribeTopic)
    }

    @Test
    fun `host 为空时配置无效`() {
        assertFalse(AppSettings(host = "").isValid)
        assertTrue(AppSettings(host = "192.168.1.10").isValid)
    }

    @Test
    fun `端口越界时配置无效`() {
        assertFalse(AppSettings(host = "h", port = 0).isValid)
        assertFalse(AppSettings(host = "h", port = 70000).isValid)
        assertTrue(AppSettings(host = "h", port = 1).isValid)
        assertTrue(AppSettings(host = "h", port = 65535).isValid)
    }

    @Test
    fun `主题为空时配置无效`() {
        assertFalse(AppSettings(host = "h", topicBase = "  ").isValid)
    }

    @Test
    fun `normalized 会去掉首尾空白`() {
        val s = AppSettings(
            host = "  10.0.0.1 ",
            topicBase = " taskops/todo/ ",
            serverName = " taskops ",
            clientId = " cid ",
        ).normalized()
        assertEquals("10.0.0.1", s.host)
        assertEquals("taskops/todo", s.topicBase)
        assertEquals("taskops", s.serverName)
        assertEquals("cid", s.clientId)
    }
}
